# 原料管理系统 WPF v4.2.3

单机桌面端原料称量管理系统。由 PyQt6 v3.0 重构为 **WPF + .NET 8**，业务功能对等，SQLite 数据库兼容。

## 项目是做什么的

实验室/产线按配方步骤扫码、称重；业务人员维护原材料与配方，系统管理员维护账号。系统对接：

- **电子天平**：RS232 / USB 串口（Modbus-RTU）
- **扫码枪**：**推荐 USB COM 串口**；兼容 HID 键盘楔入（应急回退）
- **Excel 导出**：ClosedXML（统一宋体）

两角色：`system_admin`（系统管理员）/ `business`（业务人员）。登录只输用户名密码，不再选手动身份。

## 快速入口

| 文档 | 说明 |
|------|------|
| [docs/quickstart.md](docs/quickstart.md) | **5 分钟上手**：运行、登录、主要操作 |
| [docs/PROJECT.md](docs/PROJECT.md) | **项目结构**：目录、模块、面板与功能对照 |
| [docs/TECH.md](docs/TECH.md) | **技术说明**：串口双 COM、WPF 架构、状态机 |
| [docs/wpf-tutorial.md](docs/wpf-tutorial.md) | 基于本仓库的 WPF 入门教程 |
| [DEPLOY.md](DEPLOY.md) | 发布打包、迁库、现场部署 |
| [docs/feature-checklist.md](docs/feature-checklist.md) | 功能对照清单 |
| [docs/hardware-checklist.md](docs/hardware-checklist.md) | 硬件联调检查清单 |
| [docs/improvement-plan-v4.2.md](docs/improvement-plan-v4.2.md) | **v4.2 改进计划**（已落地） |
| [WPF重构计划.md](WPF重构计划.md) | 历史重构总纲（运行时根目录哨兵，勿删） |

## 仓库结构

```
Material_Mgmt_wpf/
├── README.md / DEPLOY.md / WPF重构计划.md
├── config/settings.example.ini   # 配置模板
├── data/material_mgmt.db         # SQLite（开发时，Git 忽略）
├── logs/                         # app.log / audit.log
├── docs/                         # 文档
├── tools/publish.ps1             # 自包含发布
├── dist/                         # 发布产物（Git 忽略）
└── src/
    ├── MaterialMgmt.sln
    ├── MaterialMgmt.App             # WPF UI / 导航 / 面板
    ├── MaterialMgmt.Core            # 实体、状态机、编码、权限
    ├── MaterialMgmt.Infrastructure  # EF Core、串口、导出、配置
    └── MaterialMgmt.Tests           # 单元测试
```

## 开发环境

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022+（可选）或 CLI

```powershell
cd src
dotnet build
dotnet test
dotnet run --project MaterialMgmt.App
```

首次运行前复制配置模板：

```powershell
copy config\settings.example.ini config\settings.ini
```

也可直接运行已打包程序：

```text
dist\原料管理系统\原料管理系统.exe
```

## 初始账号（仅空库首次写入）

| 用户名 | 角色 | 说明 |
|--------|------|------|
| admin | 系统管理员 | 仅维护业务人员账号 + 系统设置 |
| operator | 业务人员 | 原料/配方/称量窗/工单 |

> 初始密码不写在仓库里，见随发布包附带的「初始账号.txt」交付单（来源为本机 `docs/local/初始账号.md`，已 gitignore）。**首次登录会强制修改**；连续输错 5 次账号锁定 60 秒。

登录不再选择身份，按账号角色进入对应导航。现场库升级时会迁移旧角色并删除 `root`（详见 [DEPLOY.md](DEPLOY.md)）。

## 发布

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\publish.ps1
```

脚本会备份并恢复 `dist` 内现有数据库与 `settings.ini`。详见 [DEPLOY.md](DEPLOY.md)。

## 架构一览

```
App (WPF 面板 + DeviceHub + NavigationService)
        │
        ▼
Infrastructure (EF Core / ScaleWorker / BarcodeSerial / Export / Config)
        │
        ▼
Core (WorkflowEngine / CodeService / OrderRules / Entities)
        │
        ▼
SQLite  data/material_mgmt.db
```

称量核心在 `WorkflowEngine`：扫码 → 等待称重 → 稳定判定 → 确认进入下一步。扫码经 `DeviceHub` 统一路由（串口或 HID）；存在进行中称量时由称量窗独占。

## 开发中（v4.3.0）：列表与配方编辑

- **列表**：五个列表页统一页头（标题 + 搜索 + 操作）；表头复选框全选当前页（部分选中显示横杠）；每页 10/20/50 条并按页面记住，可输入页码跳转；无数据时显示提示与主操作，筛选无结果时可一键清除筛选
- **工单筛选**：工单历史与我的工单可按状态、开始日期区间、配方筛选
- **配方编辑**：上移/下移即时保存（去掉「保存顺序」按钮）；步骤弹窗实时提示范围是否有效，可「保存并继续添加」连续录入
- **弹窗**：剩余手搭弹窗全部改用统一弹窗（Enter 确认 / Esc 取消 / 字段内联报错）；工单详情可调大小；删除有工单的账号时可选「仅禁用」

## 本版（v4.2.3）：称量数据可信度收尾

- **配方编辑保护**：配方有进行中工单时，编辑页锁定全部步骤操作并说明是哪几张工单；已有称重记录的步骤不能删除（防止历史记录失去对应步骤）
- **重称作废原因**：重称时选择原因（读数异常 / 放错料 / 加料量需调整 / 其他 + 备注），工单详情列出全部记录，作废记录灰显并标出作废人、时间、原因；导出仍只含有效记录
- **扫码来源**：每条称重记录记下原料码来自串口扫码枪还是键盘模式扫码枪，详情与 Excel 导出各加一列
- **备份**：除启动外，正常退出时也备份；系统管理员首页显示最近备份时间并可“立即备份”
- **数据修复入口**：旧库存在“同一步骤多条有效记录”时，系统管理员首页提示并可一键修复（先备份，保留最新一条）
- **运维**：异常弹窗不再显示调用堆栈（详情写日志）；`raw_frame_log` 开关可记录天平原始帧；断开天平不再卡界面；文档不再写明初始密码
- 新增两列 `weight_records.void_reason`、`scan_source`，启动时自动补列，旧记录留空，无需手工操作

v4.2.2：称量量程条、提示音、清零、断线横幅、暂停返回与完工汇总、工艺参数对话框；v4.2.1：登录加固、启动备份、表格多选联动、配方步骤插入。
