# 项目结构与模块对照

原料管理系统 WPF v4.2.3 — 文件、代码分层与功能映射。

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
│  Entities · WorkflowEngine · CodeService · OrderRules   │
└──────────────────────────┬──────────────────────────────┘
                           │
                    SQLite (data/material_mgmt.db)
```

## 项目职责

| 项目 | 职责 |
|------|------|
| **MaterialMgmt.App** | WPF 主窗口、导航、各业务面板、登录/对话框、设备中枢 `DeviceHub` |
| **MaterialMgmt.Core** | 实体、称量状态机 `WorkflowEngine`、编码规则 `CodeService`、工单规则 `OrderRules`、配方序号 `FormulaSequence` |
| **MaterialMgmt.Infrastructure** | EF Core 上下文、INI 配置、认证、工单服务、Excel 导出、天平/扫码串口 |
| **MaterialMgmt.Tests** | 状态机、编码、配方序号、工单删除规则、分页、工单筛选、步骤范围校验、数据库冒烟测试 |

## 界面面板 ↔ 功能

| 面板类 | 导航项 | 角色 | 主要功能 |
|--------|--------|------|----------|
| `HomePanel` | 首页 | 全部 | 业务人员：「开始称量」大按钮、全部进行中工单（本人可继续）、今日工单 / 完成 / 含重称统计、快捷入口、设备卡；系统管理员：账号概况、设备卡、“数据安全”（最近备份时间、立即备份、重复有效记录修复） |
| `MaterialsPanel` | 原材料 | business | 材料 CRUD（`MaterialEditDialog`）、批量删除 |
| `FormulasPanel` | 配方管理 | business | 配方列表、新建 / 复制配方（`FormulaCreateDialog` / `FormulaCopyDialog`）、多选启用/停用 |
| `FormulaEditorPanel` | 编辑步骤 | business | 步骤弹窗 `FormulaStepDialog`（实时范围校验、保存并继续添加）、扫码选料、拖拽或上移/下移即时保存；配方有进行中工单时锁定全部步骤操作；有称重记录的步骤不可删 |
| `AdminHistoryPanel` | 工单历史 | business | 全量历史、状态/日期/配方筛选、详情 `WorkOrderDetailDialog`（含作废记录与作废原因、扫码来源、异常说明）、导出；进行中不可删 |
| `UserMgmtPanel` | 用户管理 | system_admin | 业务人员账号 CRUD / 重置密码；删除有关联工单的账号时 `LinkedUserActionDialog` 选择仅禁用或解除关联后删除 |
| `SettingsPanel` | 系统设置 | 全部 | 秤/扫码 COM、模式、自检、提示音开关 / 音量 / 试听、改密 |
| `WeighHostWindow` | 称量窗（独立） | business | 宿主：开始称量 / 称量中 / 我的工单；记住大小与位置（`[weigh_window]`） |
| `SessionSelectPanel` | 开始称量 | business（称量窗） | 本人未完成工单提示条（可继续）、选配方、步骤表格预览、工艺参数、开工单 |
| `WeighSessionPanel` | 称量中 | business（称量窗） | 进度侧栏、量程条、提示音、扫码比对、回称、确认步骤 |
| `OperatorHistoryPanel` | 我的工单 | business（称量窗） | 继续/取消进行中、删除已完成或已取消、导出、状态/日期/配方筛选 |
| `ReworkReasonDialog` | 重称确认 | business（称量窗） | 选择作废原因（读数异常 / 放错料 / 加料量需调整 / 其他 + 备注） |

## 核心服务对照

| 类型 | 文件 | 说明 |
|------|------|------|
| 设备中枢 | `App/Services/DeviceHub.cs` | 天平连接、扫码双源、焦点/独占路由、自动重连 |
| 导航 | `App/Services/NavigationService.cs` | 页面注册、缓存、角色权限、`IBarcodeHandler` |
| 侧栏菜单 | `App/Controls/NavMenu.cs` | 主窗与称量窗共用；图标、分组标题、页面项与动作项（如在称量窗中打开） |
| 设备状态栏 | `App/Controls/DeviceStatusBar.cs` + `App/Services/DeviceStatusText.cs` | 两窗共用；异常时警示底色、点击重连；状态文字与首页设备卡同源 |
| HID 按键转发 | `App/Services/HidKeyMapper.cs` | 窗口 `PreviewKeyDown` → 键盘模式扫码枪聚合 |
| 今日统计 | `Core/Orders/TodaySummary.cs` + `OrderService.OrdersStartedOn` | 今日工单 / 完成 / 含重称（单测覆盖） |
| 称量工作区 | `App/Services/WeighWorkspace.cs` | 单称量窗生命周期；与主窗退出登录协调 |
| 称量状态机 | `Core/Workflow/WorkflowEngine.cs` | 等待扫码 → 称重 → 确认 → 下一步；回称 `RewindToStep` |
| 量程计算 | `Core/Workflow/RangeGaugeMath.cs` | 目标区间与指针比例（单测覆盖） |
| 量程条 UI | `App/Controls/RangeGauge.cs` | 称量页可视化目标范围 |
| 列表页头 | `App/Controls/PageHeader.cs` | 标题 + 搜索框（占位提示）+ 操作按钮；第二行放筛选项 |
| 分页条 | `App/Controls/PaginationBar.cs` + `Core/Lists/Paging.cs` | 每页 10/20/50 条（按页面记入 `[ui] page_size_*`）、页码跳转 |
| 空状态 | `App/Controls/EmptyState.cs` | 无数据时的提示与主操作；筛选无结果时给「清除筛选」 |
| 工单筛选 | `App/Controls/WorkOrderFilterInputs.cs` + `Core/Orders/WorkOrderFilter.cs` | 状态 / 开始日期区间 / 配方 + 关键字 |
| 行拖拽排序 | `App/Controls/RowDragReorder.cs` + `FormulaSequence.MoveTo` | 只从手柄列开始拖，显示插入线、边缘自动滚动；配方编辑页使用 |
| 表格勾选 | `App/Controls/SelectableRow.cs`（`GridKit`） | 行复选框与选中行联动；表头三态复选框全选当前页；空格切换当前行 |
| 弹窗基类 | `App/Views/Dialogs/DialogWindow.cs` | 所有弹窗统一：Owner、Enter 确认 / Esc 取消、字段内联错误、可调大小 |
| 步骤范围校验 | `Core/Formulas/StepRange.cs` | 上下限解析与校验（单测覆盖） |
| 提示音 | `App/Services/SoundService.cs` | 内存合成 WAV；受 `sound_enabled` 与 `sound_volume`（low / medium / high）控制 |
| 角色迁移 | `Core/Auth/RoleMigration.cs` | admin→system_admin，operator→business，删 root |
| 天平 | `Infrastructure/Hardware/ScaleWorker.cs` | Modbus-RTU 读重、端口探测 |
| 扫码串口 | `Infrastructure/Hardware/BarcodeSerialService.cs` | USB COM 按 CR/LF 收整帧 |
| 扫码 HID | `Infrastructure/Hardware/BarcodeInputService.cs` | 键盘楔入聚合（应急回退） |
| 配置 | `Infrastructure/Services/ConfigService.cs` | `settings.ini` 读写 |
| 数据库 | `Infrastructure/Data/DbBootstrap.cs` | 建库、启动时补列、种子账号、WAL、v4.2 角色迁移、启动 / 退出 / 手动备份、有效记录唯一索引 |
| 工单 | `Infrastructure/Services/OrderService.cs` | 保存称重（含快照、扫码来源）/完成/作废有效记录（含原因）；进行中工单及进度（`ActiveOrders`）；配方进行中引用校验；重复有效记录修复；删除走 `OrderRules` |
| 配方 | `Infrastructure/Services/FormulaService.cs` | 含复制配方 |
| 配方序号 | `Core/Formulas/FormulaSequence.cs` | `(formula_id, sequence)` 唯一索引的两阶段写入 |
| 导出 | `Infrastructure/Export/ExportService.cs` | ClosedXML Excel |

## 数据实体

| 实体 | 表 | 说明 |
|------|-----|------|
| `User` | users | 账号、bcrypt 密码、角色 |
| `Material` | materials | 原料，`MAT-` 编码 |
| `Formula` / `FormulaItem` | formulas / formula_items | 配方与步骤上下限 |
| `WorkOrder` | work_orders | 工单与工艺参数 |
| `WeightRecord` | weight_records | 逐步称重记录；含称重时限值 / 原料快照、作废时间 / 人 / 原因（`void_reason`）、扫码来源（`scan_source`：serial / hid / simulated，旧记录为空） |
| `FormulaErpBinding` | formula_erp_bindings | ERP 映射预留（UI 隐藏） |

## 扫码路由

```
扫码枪 (USB COM 或 HID)
    → DeviceHub.PublishBarcode
    → ResolveBarcodeHandler：
         进行中称量 → 称量窗独占
         否则 → 前台焦点窗（主窗或称量窗）当前面板
    → WeighSessionPanel → WorkflowEngine.OnBarcode（原料比对）
    → SessionSelectPanel → 配方码 FML- 选中
    → FormulaEditorPanel → 步骤弹窗打开时按编码选中原料
```

## 配置文件字段

| 节 | 键 | 说明 |
|----|-----|------|
| `[serial]` | port, baud_rate, auto_connect, raw_frame_log | 电子天平；`raw_frame_log = true` 时把收发原始帧写入 app.log（排查用） |
| `[scanner]` | port, baud_rate, mode, enabled | 扫码枪（mode: Auto / Serial / Hid） |
| `[app]` | window_width, window_height, window_maximized, sound_enabled, sound_volume | 主窗几何、提示音开关与音量 |
| `[weigh_window]` | left, top, width, height, maximized | 称量窗几何（超出屏幕时回到默认居中） |

首次运行：复制 `config/settings.example.ini` → `config/settings.ini`。

## 相关文档

- [quickstart.md](quickstart.md) — 5 分钟上手
- [TECH.md](TECH.md) — 技术说明（运维 + 开发）
- [hardware-checklist.md](hardware-checklist.md) — 现场联调清单
- [DEPLOY.md](../DEPLOY.md) — 打包与部署
