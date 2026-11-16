using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using MaterialMgmt.App.Services;
using MaterialMgmt.App.Views.Dialogs;
using MaterialMgmt.App.Views.Panels;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Infrastructure;
using MaterialMgmt.Infrastructure.Data;

namespace MaterialMgmt.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private DbBootstrap? _bootstrap;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 登录对话框关闭时若无其它窗口，默认 OnLastWindowClose 会直接退出应用
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += OnUnhandled;

        var paths = new Infrastructure.Services.AppPaths();
        Directory.CreateDirectory(paths.LogsDir);
        Directory.CreateDirectory(paths.DataDir);
        Directory.CreateDirectory(paths.ConfigDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(paths.AppLogPath, rollingInterval: RollingInterval.Infinite,
                shared: true, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .WriteTo.Console()
            .CreateLogger();

        var sc = new ServiceCollection();
        sc.AddLogging(b => b.AddSerilog(dispose: true));
        sc.AddMaterialMgmtInfrastructure(paths.AppRoot);
        sc.AddSingleton<NavigationService>();
        sc.AddSingleton<DeviceHub>();
        sc.AddSingleton<WeighWorkspace>();
        sc.AddTransient<HomePanel>();
        sc.AddTransient<MaterialsPanel>();
        sc.AddTransient<FormulasPanel>();
        sc.AddTransient<FormulaEditorPanel>();
        sc.AddTransient<SessionSelectPanel>();
        sc.AddTransient<WeighSessionPanel>();
        sc.AddTransient<AdminHistoryPanel>();
        sc.AddTransient<OperatorHistoryPanel>();
        sc.AddTransient<UserMgmtPanel>();
        sc.AddTransient<SettingsPanel>();
        sc.AddSingleton<MainWindow>();

        _services = sc.BuildServiceProvider();
        _bootstrap = _services.GetRequiredService<DbBootstrap>();

        try
        {
            await _bootstrap.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "数据库初始化失败");
            MessageBox.Show($"数据库初始化失败：\n{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var auth = _services.GetRequiredService<IAuthService>();
        var audit = _services.GetRequiredService<IAuditLog>();
        var login = new LoginWindow(auth, audit);
        if (login.ShowDialog() != true || login.LoggedInUser is null)
        {
            Shutdown(0);
            return;
        }

        try
        {
            var main = _services.GetRequiredService<MainWindow>();
            MainWindow = main;
            main.OnLogin(login.LoggedInUser);
            main.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            Log.Information("用户 {User} ({Role}) 已登录，主窗口已显示",
                login.LoggedInUser.Username, login.LoggedInUser.Role);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "登录后初始化主窗口失败");
            MessageBox.Show($"主窗口启动失败：\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_bootstrap is not null)
            await _bootstrap.ShutdownAsync();
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "未捕获异常");
        MessageBox.Show(e.Exception.ToString()[^Math.Min(2000, e.Exception.ToString().Length)..],
            "程序异常", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
