# 基于「原料管理系统」的 WPF 入门教程

本教程不讲空泛概念，而是对照本仓库真实代码，帮你快速建立 WPF 桌面开发直觉。建议边看边打开对应文件。

目标读者：会一点 C#，没用过或刚接触 WPF。

---

## 0. 先建立地图

本项目是 **WPF + .NET 8** 单机应用，分层如下：

| 项目 | 职责 | 你该先看 |
|------|------|----------|
| `MaterialMgmt.App` | 窗口、面板、导航、主题、设备总线 | `App.xaml.cs`、`MainWindow.xaml.cs` |
| `MaterialMgmt.Core` | 与 UI 无关的业务规则 | `WorkflowEngine.cs`、`CodeService.cs` |
| `MaterialMgmt.Infrastructure` | 数据库、串口、导出、配置 | `MaterialMgmtDbContext.cs`、`ScaleWorker.cs` |
| `MaterialMgmt.Tests` | 状态机等单元测试 | `WorkflowEngineTests.cs` |

**记住一句话**：界面在 App，规则在 Core，落地（文件/串口/SQL）在 Infrastructure。

---

## 1. WPF 应用从哪里启动？

入口是 `App.xaml` + `App.xaml.cs`。

```16:57:src/MaterialMgmt.App/App.xaml.cs
public partial class App : Application
{
    private ServiceProvider? _services;
    // ...
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // 注册 DI、初始化数据库、弹登录框、再 Show 主窗口
```

要点：

1. **`Application`**：整个进程只有一个，生命周期从启动到退出。  
2. **`ShutdownMode`**：登录窗关闭时若还没显示主窗，默认会退出进程。本项目先设为 `OnExplicitShutdown`，主窗显示后再改为 `OnMainWindowClose`。  
3. **依赖注入**：`ServiceCollection` 注册面板与服务，`BuildServiceProvider()` 解析。这不是 WPF 专有，但桌面项目很常用。

`App.xaml` 里合并了主题字典：

```xml
<ResourceDictionary Source="Themes/AppTheme.xaml"/>
```

全局样式（按钮、DataGrid 居中等）写在这里，代码里通过 `UiTheme` 工厂取按钮。

---

## 2. 窗口、控件与「代码搭 UI」

经典教程多用 XAML 写界面。本项目大量面板是 **纯 C# 构建**（`UserControl` + `new Button`），好处是逻辑集中、适合快速迭代；代价是可视化设计器不友好。

示例（设置页工具栏思路，同类见各 `*Panel.cs`）：

```csharp
var root = new DockPanel { Margin = new Thickness(20) };
var bar = new StackPanel { Orientation = Orientation.Horizontal };
DockPanel.SetDock(bar, Dock.Top);
bar.Children.Add(UiTheme.PrimaryButton("保存并连接"));
root.Children.Add(bar);
Content = root;   // UserControl 的内容根
```

你需要熟悉的布局控件：

| 控件 | 用途 |
|------|------|
| `DockPanel` | 上下左右停靠（顶栏 + 底部分页 + 中间表格） |
| `StackPanel` | 横向/纵向一排控件 |
| `Grid` | 行列表单（工艺参数对话框） |
| `Border` | 卡片、圆角边框（`UiTheme.Card`） |
| `DataGrid` | 列表（原材料、工单） |
| `ScrollViewer` | 内容过多时可滚 |

**附加属性**：`DockPanel.SetDock(child, Dock.Top)` 这种「写在子元素上、影响父布局」的写法是 WPF 特色。

---

## 3. 主壳：导航不是网页路由

`MainWindow` 左侧 `ListBox` 选中项 → `NavigationService.Navigate(AppPage.xxx)` → 右侧 `ContentControl` 换面板。

```csharp
// 概念伪代码
_host.Content = panel;           // 切换中间内容
panel 实现 IPageLifecycle → OnShow();  // 每次显示刷新数据
```

本项目约定：

- `AppPage` 枚举表示页面  
- 面板多为 `AddTransient`，但导航服务会缓存实例，换页再回来不丢状态  
- **登录时 `ClearCache()`**，避免上一用户数据残留  

操作员扫码时，主窗口把键盘事件喂给 `BarcodeInputService`，再路由到当前实现了 `IBarcodeHandler` 的面板（称量页、设置页等）。这是典型的 **壳负责输入、页负责业务**。

---

## 4. 数据绑定与本项目的「半绑定」

标准 WPF 推荐：

```xml
<TextBlock Text="{Binding MaterialName}"/>
```

本仓库列表多用：

1. `DataGrid` + `Binding("Item.Name")`（`GridKit.Text`）  
2. 其它状态用代码直接改 `_guide.Text = ...`

你要掌握：

- **Binding 路径**：`Item.Code` 表示行包装对象 `SelectableRow<T>` 上的 `Item` 属性。  
- **Dispatcher**：串口线程回调重量时，必须 `Dispatcher.Invoke` 回 UI 线程再改控件，否则会抛跨线程异常。见 `WeighSessionPanel.OnWeight`。

```csharp
Dispatcher.Invoke(() =>
{
    _weight.Text = $"{weight:F3} g";
    // 更新状态机与界面
});
```

---

## 5. 主题与样式：XAML + 代码工厂

- `Themes/AppTheme.xaml`：`Style TargetType="DataGridCell"` 等，统一行高、居中。  
- `Themes/UiTheme.cs`：`PrimaryButton` / `GhostButton` / `DangerButton`，避免每个面板手写颜色。

学习建议：改一处按钮颜色时，优先改主题，而不是单个面板。

---

## 6. 业务状态机（与 WPF 解耦）

称量流程在 Core，不引用任何 WPF 类型：

```
WaitingScan →（扫码正确）→ Weighing →（稳定且合格）→ WeightOk → Confirm → 下一步 / Complete
```

文件：`src/MaterialMgmt.Core/Workflow/WorkflowEngine.cs`。

UI 只负责：

1. 把扫码/重量事件喂给引擎  
2. 根据 `StepStatus` 刷新文案与按钮 `IsEnabled`  

这是很好的分层示范：**UI 可以换（WinForms/Avalonia），规则可测**。测试在 `MaterialMgmt.Tests`。

---

## 7. 硬件与后台线程

| 组件 | 文件 | 要点 |
|------|------|------|
| `ScaleWorker` | Infrastructure | `Task.Run` 循环读串口；事件上报重量 |
| `DeviceHub` | App | 登录后探测 COM、重连；主窗定时器 `Tick` |
| `BarcodeInputService` | Infrastructure | 聚合快速按键 + Enter |

登录后自动连秤：

```csharp
_devices.BeginAutoConnectAfterLogin(_config);
```

探测时用与正式通信相同的 Modbus 读命令，避免「口能打开但不是天平」。

---

## 8. 数据库：EF Core + SQLite

- 上下文：`MaterialMgmtDbContext`  
- 启动建库/迁移兼容：`DbBootstrap`  
- 面板里常见写法：

```csharp
using var db = _dbFactory.CreateDbContext();
var list = db.Materials.OrderByDescending(m => m.Id).ToList();
```

`IDbContextFactory` 适合桌面短生命周期操作，避免一个上下文活太久。

路径：`AppPaths` 向上查找含 `WPF重构计划.md` 的目录作为应用根，从而固定 `data\`、`config\`、`logs\`。

---

## 9. 常见模式速查（对着项目练）

| 想做的事 | 看哪里 |
|----------|--------|
| 新增一个左侧菜单页 | `MainWindow.RebuildNav` + `NavigationService` + 新 `*Panel` + DI 注册 |
| 表格加勾选/全选 | `Controls/SelectableRow.cs` → `GridKit` |
| 弹对话框拿结果 | `WorkOrderParamsDialog`：`ShowDialog()` + `DialogResult` |
| 改全局表格样式 | `AppTheme.xaml` 的 `DataGrid`/`DataGridCell` |
| 改称量判定 | `WorkflowEngine`（先写测试再改） |
| 导出 Excel | `ExportService`（ClosedXML） |

---

## 10. 建议的练习路线（1～2 天）

1. **跑起来**：`dotnet run --project MaterialMgmt.App`，用 admin / operator 各登一次。  
2. **改文案**：改 `WeighSessionPanel` 某句提示，体会 UI 刷新。  
3. **加一列**：原材料表加一列「描述长度」或只读字段（`GridKit.Text`）。  
4. **跟一次扫码**：设置页模拟扫码，断点打在 `OnBarcode`。  
5. **跟一次重量**：在 `WorkflowEngine.OnWeightUpdate` 打断点，理解稳定才判定。  
6. **读测试**：跑 `dotnet test`，对照 `HappyPath_Complete`。  

---

## 11. 和「标准教科书 WPF」的差异（避免迷路）

| 教科书常见 | 本项目实际 |
|------------|------------|
| 大量 XAML + MVVM | 大量代码 UI；未强制 CommunityToolkit.Mvvm |
| `ICommand` / `RelayCommand` | 直接 `button.Click +=` |
| 设计器拖拽 | 以代码布局为主 |
| 每页独立 ViewModel | 面板类自己持有服务与控件字段 |

这不代表 MVVM 不好；本项目优先「功能对齐旧版 PyQt、交付快」。你以后新模块可以用 XAML + MVVM，与现有面板并存。

---

## 12. 下一步阅读

1. [quickstart.md](quickstart.md) — 业务操作路径  
2. `App.xaml.cs` → `MainWindow.xaml.cs` → `WeighSessionPanel.cs` — 启动到称量主链路  
3. `WorkflowEngine.cs` + 测试 — 领域核心  
4. [DEPLOY.md](../DEPLOY.md) — 如何发布给现场  

遇到「为什么登录后闪退」「为什么控件不更新」：优先查 **ShutdownMode** 与 **Dispatcher**——本项目踩过的两个坑，也是 WPF 新手最高频的坑。
