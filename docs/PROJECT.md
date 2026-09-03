# 项目结构与模块对照

原料管理系统 WPF v4.1.5 — 文件、代码分层与功能映射。

## 仓库目录

```
Material_Mgmt_wpf/
├── README.md                 # 项目入口与文档导航
├── DEPLOY.md                 # 部署、迁库、打包说明
├── WPF重构计划.md            # 历史重构总纲（运行时根目录哨兵，勿删）
├── config/
│   ├── settings.example.ini  # 配置模板（提交到 Git）
│   └── settings.ini          # 本地运行配置（Git 忽略）
├── data/                     # SQLite 业务库（Git 忽略）
├── logs/                     # app.log / audit.log（Git 忽略）
├── docs/                     # 文档
├── tools/
│   ├── publish.ps1           # 自包含发布脚本
│   └── check_db.py           # 可选：检查数据库表
├── dist/                     # 发布产物（Git 忽略）
└── src/
    ├── MaterialMgmt.sln
    ├── MaterialMgmt.App/           # WPF 界面层
    ├── MaterialMgmt.Core/          # 领域模型与业务规则
    ├── MaterialMgmt.Infrastructure/ # 数据访问、硬件、导出
    └── MaterialMgmt.Tests/         # 单元测试
```

## 解决方案分层

```
┌─────────────────────────────────────────────────────────┐
│  MaterialMgmt.App                                       │
│  MainWindow / Panels / Dialogs / DeviceHub / Navigation │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│  MaterialMgmt.Infrastructure                            │
│  EF Core · Config · Auth · Order · Export · 串口硬件     │
└──────────────────────────┬──────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────┐
│  MaterialMgmt.Core                                      │
│  Entities · WorkflowEngine · CodeService · Abstractions │
└──────────────────────────┬──────────────────────────────┘
                           │
                    SQLite (data/material_mgmt.db)
```

## 项目职责

| 项目 | 职责 |
|------|------|
| **MaterialMgmt.App** | WPF 主窗口、导航、各业务面板、登录/对话框、设备中枢 `DeviceHub` |
| **MaterialMgmt.Core** | 实体、称量状态机 `WorkflowEngine`、编码规则 `CodeService`、权限与抽象接口 |
| **MaterialMgmt.Infrastructure** | EF Core 上下文、INI 配置、认证、工单服务、Excel 导出、天平/扫码串口 |
| **MaterialMgmt.Tests** | 状态机、编码、分页、数据库冒烟测试 |

## 界面面板 ↔ 功能

| 面板类 | 导航项 | 角色 | 主要功能 |
|--------|--------|------|----------|
| `HomePanel` | 首页 | 全部 | 设备状态概览、快捷入口 |
| `MaterialsPanel` | 原材料 | admin | 材料 CRUD、批量删除 |
| `FormulasPanel` | 配方管理 | admin | 配方列表、启用/停用 |
| `FormulaEditorPanel` | 编辑步骤 | admin | 配方步骤与上下限 |
| `SessionSelectPanel` | 开始称量 | operator | 选配方、工艺参数、开工单 |
| `WeighSessionPanel` | 称量中 | operator | 扫码比对、实时称重、确认步骤 |
| `OperatorHistoryPanel` | 我的工单 | operator | 进行中/已完成、导出、取消 |
| `AdminHistoryPanel` | 工单历史 | admin | 全量历史、异常说明、导出 |
| `UserMgmtPanel` | 用户管理 | admin | 账号 CRUD |
| `SettingsPanel` | 系统设置 | 全部 | 秤/扫码 COM、模式、自检、改密 |

## 核心服务对照

| 类型 | 文件 | 说明 |
|------|------|------|
| 设备中枢 | `App/Services/DeviceHub.cs` | 天平连接、扫码双源（串口/HID）、条码路由、自动重连 |
| 导航 | `App/Services/NavigationService.cs` | 页面注册、缓存、`IBarcodeHandler` 当前页路由 |
| 称量状态机 | `Core/Workflow/WorkflowEngine.cs` | 等待扫码 → 称重 → 确认 → 下一步 |
| 天平 | `Infrastructure/Hardware/ScaleWorker.cs` | Modbus-RTU 读重、端口探测 |
| 扫码串口 | `Infrastructure/Hardware/BarcodeSerialService.cs` | USB COM 按 CR/LF 收整帧 |
| 扫码 HID | `Infrastructure/Hardware/BarcodeInputService.cs` | 键盘楔入聚合（应急回退） |
| 配置 | `Infrastructure/Services/ConfigService.cs` | `settings.ini` 读写 |
| 数据库 | `Infrastructure/Data/DbBootstrap.cs` | 建库、种子账号、WAL |
| 工单 | `Infrastructure/Services/OrderService.cs` | 创建/恢复/完成工单 |
| 导出 | `Infrastructure/Export/ExportService.cs` | ClosedXML Excel |

## 数据实体

| 实体 | 表 | 说明 |
|------|-----|------|
| `User` | users | 账号、bcrypt 密码、角色 |
| `Material` | materials | 原料，`MAT-` 编码 |
| `Formula` / `FormulaItem` | formulas / formula_items | 配方与步骤上下限 |
| `WorkOrder` | work_orders | 工单与工艺参数 |
| `WeightRecord` | weight_records | 逐步称重记录 |
| `FormulaErpBinding` | formula_erp_bindings | ERP 映射预留（UI 隐藏） |

## 扫码路由

```
扫码枪 (USB COM 或 HID)
    → DeviceHub.PublishBarcode
    → MainWindow 路由到当前面板 IBarcodeHandler.OnBarcode
    → WeighSessionPanel → WorkflowEngine.OnBarcode（原料比对）
    → SessionSelectPanel → 配方码 FML- 选中
```

## 配置文件字段

| 节 | 键 | 说明 |
|----|-----|------|
| `[serial]` | port, baud_rate, auto_connect | 电子天平 |
| `[scanner]` | port, baud_rate, mode, enabled | 扫码枪（mode: Auto / Serial / Hid） |
| `[app]` | window_width, window_height, window_maximized | 窗口几何 |

首次运行：复制 `config/settings.example.ini` → `config/settings.ini`。

## 相关文档

- [quickstart.md](quickstart.md) — 5 分钟上手
- [TECH.md](TECH.md) — 技术说明（运维 + 开发）
- [hardware-checklist.md](hardware-checklist.md) — 现场联调清单
- [DEPLOY.md](../DEPLOY.md) — 打包与部署
