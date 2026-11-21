# 技术说明

原料管理系统 WPF v4.3.0 — 运维联调与开发架构。

---

# 第一章 运维与硬件联调

## 1.1 运行环境

| 项 | 要求 |
|----|------|
| 操作系统 | Windows 10/11 x64 |
| 运行时 | 自包含发布包**无需**安装 .NET；源码调试需 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| 数据库 | SQLite 3（WAL 模式），文件 `data/material_mgmt.db` |
| 硬件 | RS232/USB 电子天平 + USB 扫码枪（推荐 COM 模式） |

## 1.2 双串口架构

本系统同时使用**两个独立 COM 口**：

| 设备 | 协议 | 默认参数 | 配置节 |
|------|------|----------|--------|
| 电子天平 | Modbus-RTU | 9600, 8N1 | `[serial]` |
| 扫码枪 | 纯文本行（CR/LF 结尾） | 9600, 8N1 | `[scanner]` |

**禁止**将天平与扫码枪配置为同一 COM。保存时程序会校验并拒绝。

登录后程序会：

1. 自动探测并连接电子秤（可排除已配置的扫码 COM）
2. 按 `[scanner]` 配置打开扫码串口（模式为 Auto 且已填 port 时）

## 1.3 扫码枪推荐配置（YHD 系列等）

现场曾出现 **USB KBW（键盘楔入）** 扫括号码丢字（如 `WH-814(20260303)` 变成 `WH-814(260303)`），根因是 Shift 粘滞与输入法干扰。推荐改为 **USB COM**：

1. 扫说明书设置码 **`USB COM`**（勿用 USB KBW）
2. 串口参数：**9600 / None / 8 / 1**
3. 后缀：**CR**（回车）
4. 设备管理器确认 COM 号（如 COM3）
5. 串口助手连续扫 10 次，内容应完全一致

在应用 **系统设置** 中：选择扫码 COM → 模式「自动」或「仅串口」→ 保存 → **硬件自检**。

### 扫码模式说明

| 模式 | 行为 |
|------|------|
| **自动** | 已配置扫码 COM 则用串口；未配置则回退 HID 键盘 |
| **仅串口** | 只走 USB COM；失败报错 |
| **仅 HID** | 键盘楔入；复杂条码可能丢字，仅作应急 |

## 1.4 标签与原料编码

称量时扫码内容须与数据库中 `Material.Code` **完全一致**（推荐 `MAT-000001` 格式）。

- 比对前会做 Trim 与大小写忽略
- 外部码如 `WH-814(20260303)` **本期不做映射**，会一直提示「原料错误」→ 需改标签或后续加映射表

## 1.5 称量操作流程（硬件视角）

1. 操作员登录 → 状态栏显示秤与扫码通道
2. **开始称量** → 选配方 → 填工艺参数
3. 每步：扫当前原料 `MAT-` 码 → 自动比对 → 放料称重 → 稳定后确认
4. 称重中可**重扫**纠错（回到等待扫码状态）
5. 全部步骤完成 → 工单 `completed`

## 1.6 配置文件

路径：`config/settings.ini`（exe 同级 `config\`）。

模板见仓库 `config/settings.example.ini`。首次部署：

```powershell
copy config\settings.example.ini config\settings.ini
```

打包脚本 `tools/publish.ps1` 会保留已有 `settings.ini`，不会覆盖现场配置。

## 1.7 日志

| 文件 | 内容 |
|------|------|
| `logs/app.log` | 应用运行日志 |
| `logs/audit.log` | 业务审计（工单、材料等操作） |

## 1.8 打包与部署

```powershell
# 在仓库根目录
powershell -ExecutionPolicy Bypass -File .\tools\publish.ps1
```

产物：

- `dist/原料管理系统/` — 完整文件夹（须整夹分发）
- `dist/原料管理系统-v4.3.0-win-x64.zip` — 压缩包

详见 [DEPLOY.md](../DEPLOY.md) 与 [hardware-checklist.md](hardware-checklist.md)。

## 1.9 快速排障

| 现象 | 排查 |
|------|------|
| 无 COM 口 | USB 驱动、换口、设备管理器 |
| 秤已连接无读数 | 波特率 9600、Modbus 协议、线序 |
| 扫码无反应 | 是否保存扫码 COM；串口助手是否占用端口 |
| 扫码缺字 | 改用 USB COM，勿用 KBW |
| 原料错误 | 标签是否等于 `MAT-…` 编码 |
| 确认按钮灰 | 读数未稳定或超配方上下限 |
| 迁库后空数据 | 确认 `data/material_mgmt.db` 在 exe 同级 |
| 添加步骤保存失败 | 升级到 v4.2+；上下限须 > 0；长弹窗底栏应固定可见 |
| 系统管理员无原料菜单 | 正常：主数据仅业务人员；称量请开称量窗 |
| 删除工单失败 | 进行中须先取消 |

---

# 第二章 开发架构

## 2.1 技术栈

| 层级 | 技术 |
|------|------|
| UI | WPF (.NET 8)、代码后置建 UI（`UiTheme` 辅助样式） |
| DI | `Microsoft.Extensions.DependencyInjection` |
| ORM | EF Core 8 + SQLite |
| 串口 | `System.IO.Ports` |
| 日志 | Serilog（文件 + 控制台） |
| Excel | ClosedXML |
| 密码 | BCrypt.Net（兼容 PyQt 版哈希） |
| 测试 | xUnit |

## 2.2 应用启动流程

```
App.OnStartup
  → AppPaths 定位根目录（WPF重构计划.md 哨兵）
  → Serilog 初始化
  → DI 注册 Infrastructure + 面板 + DeviceHub
  → DbBootstrap.InitializeAsync（建库/种子）
  → LoginWindow
  → MainWindow.OnLogin → DeviceHub.BeginAutoConnectAfterLogin
```

根目录解析见 `Infrastructure/Services/AppPaths.cs`：从 exe 向上查找含 `WPF重构计划.md` 的目录。

## 2.3 依赖注入注册

`Infrastructure/ServiceCollectionExtensions.cs` 注册：

- `IConfigService` → `ConfigService`
- `IAuthService` → `AuthService`
- `IDbContextFactory<MaterialMgmtDbContext>`
- `BarcodeInputService`、`BarcodeSerialService`（单例）
- `OrderService`、`ExportService`、`AuditLogService`

`App.xaml.cs` 额外注册 `NavigationService`、`DeviceHub` 及各 `*Panel`。

## 2.4 DeviceHub：设备与扫码中枢

`DeviceHub` 是主窗口级单例，职责：

| 职责 | 实现要点 |
|------|----------|
| 天平 | `ScaleWorker` 后台读重；`FindScalePort` 探测时排除扫码 COM |
| 扫码串口 | `BarcodeSerialService.Open(port, baud)` |
| 扫码 HID | `BarcodeInputService`；`ShouldAcceptHidKeys` 控制是否喂键 |
| 模式切换 | `ApplyScannerConfig` 根据 `ScannerInputMode` 选路 |
| 条码发布 | `PublishBarcode(code, source)`：记录 `LastScanSource`（`serial` / `hid` / `simulated`），在 UI 线程分发 |
| 定时任务 | `Tick`：扫码空闲检测、串口重连、秤自动重连 |

## 2.5 扫码路由到业务页

`DeviceHub` 不直接认识页面，由主窗在 `WireBarcodeRouting` 里注入两个回调：

```csharp
// MainWindow.xaml.cs
_devices.IsBarcodeExclusive = () => _weigh.ExclusiveBarcode;   // 称量进行中
_devices.ResolveBarcodeHandler = ResolveBarcodeHandler;          // 选出接收扫码的页面
```

分发顺序（`DeviceHub.DispatchBarcode`）：

1. `ResolveBarcodeHandler()` 选出处理器：称量进行中 → 称量窗当前页；否则跟随**前台窗口**（称量窗在前台 → 称量窗当前页，主窗在前台 → 主窗当前页）。
2. 调用 `handler.OnBarcode(code, simulated)`。
3. 非称量独占时，再广播 `BarcodeRouted` 事件；`BarcodeFillHelper` 用它把扫码填入弹窗里的编码框（如原料编辑对话框）。

仅**当前页**收到扫码，避免后台页误触发。列表页扫码写入筛选框；编码弹窗扫码自动勾选手工指定并填入；配方编辑步骤弹窗打开时，扫原料码会选中对应材料。称量页拒绝 `simulated = true` 的码，接受的扫码来源随称重记录写入 `weight_records.scan_source`。

## 2.6 WorkflowEngine 状态机

```
WaitingScan ──扫对 MAT 码──► Weighing
     ▲                            │
     │         稳定且范围内        ▼
     └──重扫纠错──────── WeightOk ──确认──► NextStep / Completed
                              │
                         超范围 ► OutOfRange
```

关键 API：

- `OnBarcode(code)` — 等待扫码时比对 `CurrentItem.Material.Code`；称重态可重扫回到 `WaitingScan`
- `OnWeightUpdate(weight, stable)` — 更新读数与超范围判定
- `TryConfirm()` — 稳定且在范围内时写入步骤
- `Resume(workOrder, items)` — 从中断步骤恢复

## 2.7 电子天平协议（ScaleWorker）

读重请求（示例）：`01 03 00 00 00 04 44 09`  
置零（示例）：`01 06 00 04 00 01 09 CB`  
CRC16 Modbus。若现场天平协议不同，需修改 `ScaleWorker`。

`FindScalePort` 对各 COM 发送探测帧，首个有合法响应的口视为天平。

## 2.8 BarcodeSerialService 分帧

`DataReceived` 回调中按字符累积，遇 `\r` 或 `\n` 输出一行（长度 ≥ 4）。  
与 HID 路径在 `DeviceHub` 汇合，统一 `NormalizeCode`（Trim）。

## 2.9 数据库

- 上下文：`MaterialMgmtDbContext`
- 启动：`DbBootstrap` 确保表存在、写入种子账号（空库时）；已有账号的自定义显示名不会被覆盖（仅迁移历史名「李园涛」）
- `formula_items` 对 `(formula_id, sequence)` 有唯一索引；调整步骤顺序时用 `FormulaSequence.BuildTwoPhase` 先写入临时序号再写 1..n，避免中间态冲突
- 兼容 PyQt v3.0 的 `material_mgmt.db`，可直接拷贝使用

## 2.10 开发与测试

```powershell
cd src
dotnet build
dotnet test
dotnet run --project MaterialMgmt.App
```

测试重点：

- `WorkflowEngineTests` — 扫码比对、重扫纠错、恢复工单
- `CodeServiceTests` — MAT/FML 编码生成
- `FormulaSequenceTests` — 步骤顺序两阶段写入
- `OrderRulesTests` — 进行中工单不可删除
- `DbSmokeTests` — 数据库初始化

## 2.11 扩展建议

| 需求 | 建议触点 |
|------|----------|
| 外部条码映射 | `WorkflowEngine.OnBarcode` 或新材料字段 |
| 新硬件 | 新 `*Service` + `DeviceHub` 集成 |
| 新面板 | 实现 `IPageLifecycle` / `IBarcodeHandler`，在 `MainWindow` 注册导航 |
| ERP 对接 | 已有 `formula_erp_bindings` 表，可增 Infrastructure 服务 |

## 2.12 相关源码索引

| 主题 | 路径 |
|------|------|
| 模块对照 | [PROJECT.md](PROJECT.md) |
| WPF 入门（本仓库） | [wpf-tutorial.md](wpf-tutorial.md) |
| 功能清单 | [feature-checklist.md](feature-checklist.md) |
