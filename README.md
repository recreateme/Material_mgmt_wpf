# 原料管理系统 WPF v4.1.3

单机桌面端原料称量管理系统。由 PyQt6 v3.0 重构为 **WPF + .NET 8**，业务功能对等，SQLite 数据库兼容。

## 项目是做什么的

实验室/产线操作员按配方步骤扫码、称重；管理员维护原材料、配方与用户，并查看工单历史。系统对接：

- **电子天平**：RS232 / USB 串口（Modbus-RTU）
- **扫码枪**：**推荐 USB COM 串口**；兼容 HID 键盘楔入（应急回退）
- **Excel 导出**：ClosedXML（统一宋体）

三角色：`root`（开发）/ `admin`（管理）/ `operator`（实验）。

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

## 默认账号（仅空库首次写入）

| 用户名 | 密码 | 角色 | 登录入口 |
|--------|------|------|----------|
| root | root666 | 开发人员 | 任意 |
| admin | admin123 | 管理人员 | 管理员 |
| operator | operator123 | 实验人员 | 操作员 |

现场库若已迁移，以实际账号为准。

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

称量核心在 `WorkflowEngine`：扫码 → 等待称重 → 稳定判定 → 确认进入下一步。扫码经 `DeviceHub` 统一路由（串口或 HID）。
