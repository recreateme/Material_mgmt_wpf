# 原料管理系统 — WPF 重构实施计划

> **说明（2026-08）：** 本文为重构期总纲与历史记录，阶段任务已完成。  
> 日常开发请优先阅读 [README.md](README.md)、[docs/quickstart.md](docs/quickstart.md)、[docs/wpf-tutorial.md](docs/wpf-tutorial.md)。  
> **请勿删除本文件**：运行时 `AppPaths` 用文件名 `WPF重构计划.md` 定位仓库根目录。

| 项 | 内容 |
|----|------|
| 文档版本 | v1.0（计划） / 实现状态见 README |
| 编写日期 | 2026-08-19 |
| 源项目 | `D:\工作记录\3-AI应用课题\3厂原料管理\material-mgmt-desktop`（PyQt6 v3.0） |
| 重构目标目录 | `D:\Develop\Material_Mgmt` |
| 目标技术 | **WPF**（.NET 8 / Windows） |
| 硬性约束 | **现有业务模块必须全部保留**；兼容现有 SQLite 库与现场数据迁移 |

---

## 1. 背景与目标

### 1.1 现状摘要

源项目是运行于 Windows 实验室 PC 的**原料称量管理桌面程序**（v3.0）：

- 前身：v2.0 B/S（FastAPI + React + PostgreSQL）
- 现状：Python + **PyQt6** 独立桌面端，本机 **SQLite（WAL）**，无服务端依赖
- 硬件：RS232 电子天平（Modbus-RTU）、USB HID 扫码枪
- 角色：`root` / `admin` / `operator`
- 打包：PyInstaller 文件夹模式分发

### 1.2 重构目标

| 目标 | 说明 |
|------|------|
| UI 框架切换 | PyQt6 → **WPF**（XAML + C#），获得更好的 Windows 原生体验与 VS 工具链支持 |
| 模块完整保留 | 下文「模块清单」中的全部功能、权限、数据表、硬件能力均需落地 |
| 数据兼容 | 可直接打开现有 `material_mgmt.db`（或经受控迁移后打开），现场工单/配方不丢 |
| 可维护性 | 清晰分层（UI / ViewModel / Domain / Infrastructure），便于后续 ERP 对接 |
| 可分发性 | 单机部署（文件夹或 MSIX/单文件），免装 Python 运行时 |

### 1.3 非目标（本期不做）

- 不改回 B/S 架构，不引入必须在线的中心库
- 不启用库文件加密（SQLCipher 等）——除非现场另行立项
- 不恢复 ERP 映射 UI（库表 `formula_erp_bindings` **必须保留**，界面仍隐藏）
- 不改变「去皮由电子秤硬件完成、软件不干预」的现场约定

### 1.4 推荐开发环境

本机已安装：VS Code、**Visual Studio 2026**、PyCharm、Cursor。

| 用途 | 推荐 IDE |
|------|----------|
| WPF 主开发（XAML 设计器、调试、发布） | **Visual Studio 2026** |
| 方案/文档/脚本编辑、对照 Python 源码 | Cursor / VS Code |
| 对照旧版业务逻辑（只读） | PyCharm 或 Cursor 打开源项目 |

建议工作负载：`.NET 桌面开发`（含 WPF）。目标框架：**.NET 8 LTS**（长期支持，与 VS 2026 匹配）。

---

## 2. 源项目盘点（必须全部保留）

### 2.1 技术栈对照

| 层级 | 现有（PyQt6） | 重构目标（WPF） |
|------|---------------|-----------------|
| UI | PyQt6 + QSS | WPF + XAML + 资源字典样式 |
| 入口 | `main.py` | `App.xaml` / `App.xaml.cs` |
| ORM | SQLAlchemy 2 | EF Core 8（SQLite）或 Dapper + 手工 SQL |
| 数据库 | SQLite 3 WAL | **同一 SQLite 文件**（WAL + FK） |
| 迁移 | Alembic `0001`–`0006` | EF Migrations **对齐现有 schema**，或启动时校验 `alembic_version` |
| 串口 | pyserial + `QThread` | `System.IO.Ports` + `BackgroundService`/`Task` |
| 扫码 | Qt 全局事件过滤器 | WPF 预览键事件 / `InputManager` 钩子 |
| 密码 | bcrypt | BCrypt.Net-Next（兼容现有哈希） |
| Excel | openpyxl | ClosedXML 或 EPPlus |
| 配置 | `config/settings.ini` | 同路径 INI，或兼容读取后写回 |
| 打包 | PyInstaller | `dotnet publish`（自包含）或 MSIX |

### 2.2 数据表（全部保留）

| 表 | 用途 | 保留要求 |
|----|------|----------|
| `users` | 账号与角色 | 保留字段与 bcrypt 哈希兼容 |
| `materials` | 原材料（`MAT-`） | CRUD + 批量删除规则不变 |
| `formulas` / `formula_items` | 配方与称量步骤上下限 | 编辑器能力完整迁移 |
| `work_orders` | 工单 + 工艺参数 + 异常说明 | 字段全集迁移 |
| `weight_records` | 逐步称重记录 | 状态机写入逻辑一致 |
| `formula_erp_bindings` | ERP 映射预留 | **表保留、UI 隐藏** |
| `alembic_version` | 迁移版本 | 兼容读取；WPF 侧可继续维护或切换为 EF 版本表 |

**工单工艺参数字段（必须保留）：**  
`temperature`、`humidity`、`production_no`、`d50`、`mixing_exception` / `mixing_exception_note`、`base_film`、`batch_no`、`thickness`、`coating_exception` / `coating_exception_note`。

### 2.3 业务服务模块清单（必须保留）

| 源文件 | 职责 | WPF 对应模块建议 |
|--------|------|------------------|
| `app/auth.py` | 登录、内存会话、角色校验、密码哈希 | `MaterialMgmt.Core` / `AuthService` + `ICurrentUser` |
| `app/config.py` | `settings.ini` 读写 | `ConfigService` |
| `app/database.py` | 引擎、会话、init/shutdown、种子账号 | `DbBootstrap` + `UnitOfWork` |
| `app/models.py` | ORM 实体 | `Entities/*` |
| `services/workflow_engine.py` | 称重状态机 | `WorkflowEngine`（纯 C#，无 UI 依赖） |
| `services/scale_worker.py` | 天平串口轮询（Modbus） | `ScaleWorker` |
| `services/barcode_filter.py` | HID 扫码聚合 | `BarcodeInputService` |
| `services/code_service.py` | `MAT-` / `FML-` 生成与校验 | `CodeService` |
| `services/order_service.py` | 工单取消/删除 | `OrderService` |
| `services/export_service.py` | Excel 单条/批量导出 | `ExportService` |
| `services/audit_log.py` | `logs/audit.log` | `AuditLogService` |

### 2.4 UI 模块清单（必须保留）

#### 主壳与导航

| 源文件 | 功能要点 |
|--------|----------|
| `ui/main_window.py` | 左侧导航 + 右侧内容栈；角色过滤；登录重建面板；天平连接；扫码路由；状态栏设备指示；改密/退出；窗口几何记忆 |
| `ui/styles.py` | 全局视觉主题 | 迁为 `Themes/AppTheme.xaml` |

#### 对话框

| 源文件 | 功能要点 |
|--------|----------|
| `dialogs/login_dialog.py` | 管理员/操作员入口；`root` 不受入口限制 |
| `dialogs/change_password_dialog.py` | 各角色自助改密 |
| `dialogs/work_order_params_dialog.py` | 开始称量前工艺参数（含混料/涂布异常勾选与说明） |

#### 业务面板

| 索引 | 源面板 | 角色 | 必须保留的能力 |
|------|--------|------|----------------|
| 0 | `HomePanel` | 全部 | 欢迎语、按角色入口卡片、统计、设备状态、进行中工单快捷入口 |
| 1 | `MaterialsPanel` | admin/root | 列表/筛选/分页、新增编辑删除、批量删除、自动/手工编码 |
| 2 | `FormulasPanel` | admin/root | 列表、新建、启用停用、批量删除、进入编辑器 |
| 3 | `FormulaEditorPanel` | admin/root | 步骤增删改排序、材料选择、上下限（绝对值/中心±容差）、保存 |
| 4 | `SessionSelectPanel` | operator/root | 选配方 / 扫 `FML-` / 预览步骤 → 填参数 → 开工单 |
| 5 | `WeighSessionPanel` | operator/root | 状态机 UI、实时克重、扫 `MAT-`、确认称重、恢复中断工单、取消 |
| 6 | `AdminHistoryPanel` | admin/root | 全量历史、筛选分页、详情、编辑异常说明、导出/批量导出、删除 |
| 7 | `OperatorHistoryPanel` | operator/root | 「我的」工单、恢复进行中、取消/删除（未完成）、详情、导出 |
| 8 | `UserMgmtPanel` | admin/root | admin 仅管 operator；root 可管 admin+operator；增删改、禁用、重置密码 |
| 9 | `SettingsPanel` | 全部 | COM 口列表、测试连接、自动连接、扫码测试/模拟、改密入口 |

#### 共用控件

| 源文件 | 功能要点 |
|--------|----------|
| `ui/widgets/table_kit.py` | 稳定滚动表格、勾选列、操作按钮列、分页条 | 迁为 `Controls/*` 用户控件 |

### 2.5 权限矩阵（行为必须一致）

| 能力 | root | admin | operator |
|------|:----:|:-----:|:--------:|
| 首页 / 设置 / 改密 | ✓ | ✓ | ✓ |
| 原材料 / 配方 / 配方编辑 | ✓ | ✓ | |
| 用户管理 | ✓（admin+operator） | ✓（仅 operator） | |
| 工单历史（全量、删除完成单） | ✓ | ✓ | |
| 开始称量 / 称量作业 | ✓ | | ✓ |
| 我的工单 | ✓ | | ✓ |

### 2.6 称重状态机（行为必须一致）

```
WAITING_SCAN → WEIGHING → WEIGHT_OK（确认）→ 下一步 / COMPLETE
                      ↘ OUT_OF_RANGE（超范围，禁止确认）
```

- 扫码必须匹配当前步骤材料码 `MAT-…`
- 去皮在硬件完成，软件不写去皮流程
- 支持从已有 `in_progress` 工单 **resume**
- 审计：关键工单/账户/配方/原材料操作写入 `logs/audit.log`

### 2.7 部署与运行时约定（必须保留）

相对应用根目录：

```
原料管理系统/
├── MaterialMgmt.exe          # 或同名发布产物
├── data/material_mgmt.db     # 业务库（可含 -wal/-shm）
├── config/settings.ini       # 串口与窗口
└── logs/app.log, audit.log
```

- 退出时关闭连接并 **WAL checkpoint / dispose**，保证拷库完整
- 种子账号：**仅空库首次**写入；不复活已删账号、不覆盖密码/启用状态
- 默认种子与业务账号策略与现网一致（见源项目 `QUICKSTART.md`）

---

## 3. 目标架构（WPF）

### 3.1 解决方案结构

```
D:\Develop\Material_Mgmt\
├── WPF重构计划.md                          # 本文档
├── README.md                               # 后续补充
├── src/
│   ├── MaterialMgmt.sln
│   ├── MaterialMgmt.App/                   # WPF 启动项目（Views / ViewModels / Themes）
│   ├── MaterialMgmt.Core/                  # 领域：状态机、编码、权限 DTO、接口
│   ├── MaterialMgmt.Infrastructure/        # EF Core、串口、Excel、INI、审计日志
│   └── MaterialMgmt.Tests/                 # 单元测试（状态机、编码、权限优先）
├── tools/                                  # 可选：库结构校验脚本、对照检查表
└── docs/                                   # 可选：迁移对照、验收用例
```

### 3.2 分层与依赖方向

```
App (WPF)
  → Core（纯业务，不引用 WPF）
  → Infrastructure（实现 Core 接口）
```

采用 **MVVM**（推荐 CommunityToolkit.Mvvm）：

- View：仅绑定与命令，无业务 SQL
- ViewModel：页面状态、导航、调用服务
- Service：与现 Python services 一一对应

### 3.3 关键技术选型建议

| 决策点 | 推荐 | 理由 |
|--------|------|------|
| UI 模式 | MVVM + 导航服务 | 对齐现有「栈式面板 + navigate_to」 |
| ORM | **EF Core 8 + SQLite** | 与现模型接近；需做 schema 对齐 |
| DI | `Microsoft.Extensions.DependencyInjection` | 官方、易测 |
| 密码 | BCrypt.Net-Next | **必须能校验现有 password_hash** |
| Excel | ClosedXML | API 清晰，免许可证困扰 |
| 日志 | Serilog → `logs/app.log` | 与现双通道日志接近 |
| 串口线程 | `Task` + `IProgress`/`Channel` 回 UI 线程 | 替代 `QThread` + Signal |
| 扫码 | `PreviewKeyDown` 全局处理 + 超时缓冲 | 替代 Qt EventFilter |

### 3.4 与现库兼容策略（优先方案）

**方案 A（推荐）：以现有 SQLite schema 为准**

1. 用现库（或 Alembic head）导出完整表结构作为「契约」
2. EF 实体字段、类型、可空性与现表 **逐列对齐**
3. 启动时检查：
   - 若存在 `alembic_version` 且 revision ≥ `0006` → 直接使用
   - 若旧库无 version → 复刻 Python 侧 `_stamp_legacy_db_if_needed` 逻辑
4. 后续增量变更用 EF Migration，同时在文档中记录与 Alembic 的对应关系

**方案 B：** 启动时仍调用外部迁移工具 —— 不推荐（增加 Python 依赖，违背 WPF 独立分发）。

**禁止：** 用 `EnsureCreated` 重建空表导致现场数据被旁路或结构漂移。

---

## 4. 模块映射与实施范围

### 4.1 基础设施层

| 工作项 | 验收标准 |
|--------|----------|
| 路径解析（开发 / 发布） | `data`/`config`/`logs` 相对 exe 根 |
| Db 上下文 + WAL/FK PRAGMA | 与现行为一致 |
| 会话/UnitOfWork | 成功提交、异常回滚、退出 dispose |
| 种子数据 | 仅空库写入；业务账号同步规则不变 |
| Config INI | 可读写现有 `settings.ini` 键 |
| Audit / App 日志 | 路径与格式可读兼容 |

### 4.2 领域与服务层

逐项移植并编写单元测试：

1. `CodeService`（格式、唯一性、自动号）
2. `WorkflowEngine`（含 resume、超范围禁止确认）
3. `OrderService`（取消/删除/批量删除保护规则）
4. `AuthService`（角色、登录失败、禁用用户）
5. `ExportService`（单条/批量、工艺参数列、超范围标红）
6. `ScaleWorker`（Modbus 读重、置零、断线检测）
7. `BarcodeInputService`（快速键入+Enter，≥4 字符）

### 4.3 表现层（按导航顺序交付）

建议按依赖从少到多交付，保证每阶段可演示：

1. 登录 + 主壳导航 + 设置（串口/扫码测试）
2. 用户管理 + 改密
3. 原材料 + 配方列表 + 配方编辑器
4. 开始称量 + 工艺参数 + 称量作业（含硬件联调）
5. 我的工单 + 工单历史 + Excel 导出
6. 首页聚合与设备状态联动
7. 发布包与数据迁移演练

---

## 5. 分阶段实施计划

### 阶段 0：准备与基线（约 2–3 天）

| 任务 | 产出 |
|------|------|
| 在 `D:\Develop\Material_Mgmt` 创建解决方案骨架 | `MaterialMgmt.sln` 与三层项目 |
| 导出源库 schema / 样例数据（脱敏）对照表 | `docs/schema-baseline.md` |
| 建立功能对照检查表（本文 §2 勾选） | `docs/feature-checklist.md` |
| 约定编码规范、分支策略 | 简短 CONTRIBUTING 或 README 节 |

**里程碑 M0：** 空壳 WPF 窗口可运行；能打开只读现有 SQLite 并列出 `materials` 前 N 条。

---

### 阶段 1：数据与认证底座（约 1 周）

| 任务 | 对应源模块 |
|------|------------|
| EF 实体与 DbContext | `models.py` / `database.py` |
| 启动迁移/校验 + 种子 | `init_db` / `_seed_defaults` |
| 登录对话框 + 会话 | `login_dialog` / `auth.py` |
| 主窗口导航骨架 + 角色过滤 | `main_window` 导航部分 |
| 设置：改密 | `change_password_dialog` |

**里程碑 M1：** 三角色可登录；导航项按角色显示；改密生效；退出后库文件可安全拷贝。

---

### 阶段 2：主数据管理（约 1–1.5 周）

| 任务 | 对应源模块 |
|------|------------|
| 原材料 CRUD + 编码 | `materials_panel` + `code_service` |
| 配方列表 + 启停 + 删除 | `formulas_panel` |
| 配方编辑器（步骤编辑） | `formula_editor_panel` |
| 用户管理权限边界 | `user_mgmt_panel` |
| 表格分页/勾选共用控件 | `table_kit` |
| 审计日志埋点 | `audit_log` |

**里程碑 M2：** 管理员可完成「建材料 → 建配方 → 编步骤 → 管操作员」全流程；审计可查。

---

### 阶段 3：硬件与称量核心（约 1.5–2 周）

| 任务 | 对应源模块 |
|------|------------|
| 串口枚举、测试连接、自动重连 | `settings_panel` + `scale_worker` + `main_window` 设备监控 |
| 扫码服务与路由 | `barcode_filter` + `on_barcode` |
| 选配方 / 扫配方码 | `session_select_panel` |
| 工艺参数对话框 | `work_order_params_dialog` |
| 称量面板 + 状态机 | `weigh_session_panel` + `workflow_engine` |
| 中断恢复 | `WorkflowEngine.resume` |

**里程碑 M3：** 真机（或模拟串口）完成「扫码 → 称重 → 确认 → 完工」；超范围不可确认；可恢复进行中工单。

---

### 阶段 4：历史、导出与首页（约 1 周）

| 任务 | 对应源模块 |
|------|------------|
| 操作员历史 | `operator_history_panel` |
| 管理员历史 + 异常说明编辑 | `admin_history_panel` |
| Excel 导出（单/批） | `export_service` |
| 首页统计与快捷入口 | `home_panel` |
| 状态栏/首页设备状态同步 | `main_window` 信号等价物 |

**里程碑 M4：** 功能对照检查表业务项 100% 勾选（ERP UI 仍隐藏）。

---

### 阶段 5：硬化、发布与切换（约 1 周）

| 任务 | 说明 |
|------|------|
| 异常全局处理 | 等价 `excepthook`：写日志 + 弹窗 |
| 高 DPI / 窗口几何 | 对齐 `settings.ini` |
| `dotnet publish` 自包含 | 对标 PyInstaller onedir |
| 跨机迁库演练 | 按源 `DEPLOY.md` 流程验证 |
| 与旧版并行验收 | 同一数据库先后用旧/新程序打开（注意版本只升不降） |
| 文档迁移 | README / QUICKSTART / DEPLOY / ARCHITECTURE 的 WPF 版 |

**里程碑 M5：** 现场可替换部署；验收签字；旧 PyQt 版冻结为只读对照。

---

## 6. 详细任务分解（实施清单）

以下为实施时的勾选列表，**不得删减模块**，仅可调整顺序。

### 6.1 工程与基础设施

- [ ] 创建解决方案与项目引用关系
- [ ] 配置 DI、日志、全局异常
- [ ] 实现应用根路径（Debug vs Publish）
- [ ] SQLite + WAL + FK
- [ ] Schema 对齐与启动校验
- [ ] 种子账号逻辑
- [ ] `settings.ini` 兼容读写
- [ ] `app.log` / `audit.log`

### 6.2 认证与权限

- [ ] bcrypt 校验/哈希兼容
- [ ] `CurrentUser` 进程内会话
- [ ] `require_role` 等价守卫（导航 + 命令）
- [ ] 登录入口角色限制 + root 例外
- [ ] 退出登录清空会话并重建导航

### 6.3 主数据

- [ ] 原材料：增删改查、筛选、分页、批量删、编码
- [ ] 配方：列表、新建、启停、批量删
- [ ] 配方编辑：步骤 CRUD、排序、材料选择、范围计算、保存事务
- [ ] 用户：按角色范围管理、禁用、重置密码、防删自身等边界（对齐现实现）

### 6.4 称量业务

- [ ] 选配方 / 扫 `FML-` / 预览
- [ ] 工艺参数校验（必填项与现对话框一致）
- [ ] 创建工单写工艺字段
- [ ] 状态机全状态与提示文案对齐
- [ ] 实时重量 UI、稳定判定、超范围锁定确认
- [ ] 确认写 `weight_records`、完工写 `end_time/status`
- [ ] 取消工单 / 删除规则
- [ ] Resume 进行中工单

### 6.5 历史与导出

- [ ] 管理员历史：筛选、详情、异常说明保存、删、导出
- [ ] 操作员历史：仅本人、恢复、取消、导出
- [ ] Excel 列与样式（超范围标红、多 sheet 批量）

### 6.6 硬件与设置

- [ ] COM 列表刷新、测试线程、保存、自动连接
- [ ] 天平轮询协议字节与 CRC（对齐 `_READ_CMD` / `_ZERO_CMD`）
- [ ] 扫码超时聚合、模拟扫码、状态指示（空闲警告）
- [ ] 设备状态栏 + 首页同步

### 6.7 壳与体验

- [ ] 左侧导航 + 内容区切换（Frame 或 ContentControl）
- [ ] 子页：配方编辑、称量作业（无独立导航项）
- [ ] 主题资源字典（替代 QSS）
- [ ] 图标 `cat.ico`（或同等资源）
- [ ] 分页与表格共用控件

### 6.8 发布与运维

- [ ] 自包含发布目录结构
- [ ] 备份/迁移说明（沿用 DEPLOY 要点）
- [ ] 功能对照验收
- [ ] 性能冒烟（大表分页、串口 5Hz UI 不卡顿）

---

## 7. 风险与对策

| 风险 | 影响 | 对策 |
|------|------|------|
| EF 模型与现库细微差异 | 读写失败/静默截断 | 阶段 0 做列级对照；集成测试用真实库副本 |
| bcrypt 哈希不兼容 | 无法登录现账号 | 用现库哈希做单元测试；锁定库版本 |
| 扫码在 WPF 焦点模型下重复/丢失 | 称量卡死 | 只在隧道预览阶段处理一次；加模拟扫码回归 |
| 串口协议移植偏差 | 读数错误 | 与 PyQt `ScaleWorker._parse` 字节级对照；保留原始帧日志开关 |
| UI 线程阻塞 | 卡顿 | 串口与导出一律后台；UI 用 Dispatcher/IProgress |
| 双版本同时写同一库 | 损坏 | 文档明确禁止；切换窗口期只跑一个进程 |
| 范围蔓延（借机做 ERP） | 延期 | ERP 表保留、UI 继续隐藏，单独立项 |

---

## 8. 测试与验收

### 8.1 测试分层

| 层级 | 重点 |
|------|------|
| 单元测试 | 状态机、编码、订单删除规则、权限判断 |
| 集成测试 | 真实 SQLite 文件：登录、CRUD、工单全流程 |
| 硬件测试 | 天平 COM、扫码枪实机；断线重连 |
| 迁移测试 | 从 PyQt 生产库副本启动 WPF，数据只读核对后做一笔新工单 |
| UI 验收 | 按 §2.4 / §2.5 角色走查 |

### 8.2 验收通过标准

1. §6 实施清单全部勾选（ERP UI 除外且表仍在）
2. 现网库副本可登录原账号，历史工单可导出且字段齐全
3. 称重闭环与超范围禁止确认行为与 v3.0 一致
4. 打包目录拷贝到干净 Windows 10/11 可运行
5. 退出后拷贝 `data/` 不丢最近写入

---

## 9. 目录与文档落地约定

重构项目根目录 `D:\Develop\Material_Mgmt`：

| 路径 | 说明 |
|------|------|
| `WPF重构计划.md` | **本文**（实施总纲） |
| `src/` | 源代码解决方案（阶段 0 创建） |
| `docs/feature-checklist.md` | 功能对照勾选（阶段 0 从本文派生） |
| `docs/schema-baseline.md` | 表结构基线 |
| `README.md` 等 | 阶段 5 从源项目文档改写为 WPF 版 |

源项目 `material-mgmt-desktop` **保持只读对照**，直至 M5 切换完成；不要在源目录内直接改成 WPF。

---

## 实施状态（2026-08-19 更新）

已在本目录落地 **WPF v4.0 可运行骨架与业务主路径**：

- 解决方案：`src/MaterialMgmt.sln`（App / Core / Infrastructure / Tests）
- 已实现：登录与三角色导航、原材料/配方/配方编辑、用户管理、称量选配方与状态机、工单历史与 Excel 导出、串口与扫码基础设施、首页与设置
- 验证：`dotnet build` 通过；`dotnet test` 9 项通过（含对现有 `data/material_mgmt.db` 的冒烟）
- 启动：`dotnet run --project src/MaterialMgmt.App`
- 发布：`tools/publish.ps1` → `dist/原料管理系统/`（自包含 win-x64，已验证生成 exe）
- 阶段 5 增补：列表分页条、天平自动重连、DEPLOY.md

待办：实验室真机（天平/扫码）联调签字。

---

## 10. 工作量粗估

| 阶段 | 人天（单人熟悉业务） |
|------|----------------------|
| 阶段 0 准备 | 2–3 |
| 阶段 1 底座 | 5–7 |
| 阶段 2 主数据 | 5–8 |
| 阶段 3 称量+硬件 | 8–12 |
| 阶段 4 历史导出首页 | 5–7 |
| 阶段 5 发布切换 | 4–6 |
| **合计** | **约 30–43 人天** |

有现成 Python 行为对照时取下限；首次接触串口/扫码取上限。

---

## 11. 立即下一步（建议）

1. 用 **Visual Studio 2026** 在本目录创建 `src/MaterialMgmt.sln`（WPF + 类库）
2. 从源库复制一份 `material_mgmt.db` 到 `docs/samples/`（或本地 `data/`）作只读基线
3. 建立 `docs/feature-checklist.md`，按 §6 拆到项目管理工具
4. 启动 **阶段 1**：DbContext + 登录 + 主壳

---

## 附录 A：源项目关键路径速查

| 类型 | 路径 |
|------|------|
| 入口 | `material-mgmt-desktop/main.py` |
| 架构说明 | `ARCHITECTURE.md` |
| 部署迁移 | `DEPLOY.md` |
| 模型 | `app/models.py` |
| 状态机 | `app/services/workflow_engine.py` |
| 天平 | `app/services/scale_worker.py` |
| 主窗口 | `app/ui/main_window.py` |
| 迁移脚本 | `app/migrations/versions/0001`–`0006` |

## 附录 B：版本命名建议

| 版本 | 含义 |
|------|------|
| v3.0 | 现 PyQt6 桌面版（基线） |
| v4.0 | WPF 重构版（功能对等） |
| v4.x | WPF 上的增强（ERP UI、库加密等需单独立项） |

---

*本文档为实施总纲；具体接口命名与类图可在阶段 0/1 细化，但不得缩减 §2 所列模块范围。*
