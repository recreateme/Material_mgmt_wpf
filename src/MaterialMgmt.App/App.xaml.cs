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

/// <summary>
/// 程序入口。先定位根目录和日志，再注册服务、初始化数据库，然后显示登录窗。
/// 登录成功前关闭方式是显式退出，避免登录框一关进程就结束。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _services;
    private DbBootstrap? _bootstrap;
    private string? _logPath;
    private bool _loggedIn;

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
        _logPath = paths.AppLogPath;

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
        sc.AddSingleton<SoundService>();
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
        var login = new LoginWindow(auth, _services.GetRequiredService<IConfigService>(), audit);
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
            _loggedIn = true;
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

    protected override void OnExit(ExitEventArgs e)
    {
        var boot = _bootstrap;
        if (boot is not null)
        {
            var loggedIn = _loggedIn;
            // 进程即将结束：同步等待（后台线程执行，避免 UI 线程上的 async 续体在退出时丢失）
            var done = Task.Run(async () =>
            {
                if (loggedIn) await boot.BackupAsync("exit");
                await boot.ShutdownAsync();
            }).Wait(TimeSpan.FromSeconds(30));
            if (!done) Log.Warning("退出备份 / WAL checkpoint 超时（30 秒），已跳过");
        }
        _services?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "未捕获异常");
        var logPath = _logPath ?? "logs\\app.log";
        MessageBox.Show(
            $"程序遇到意外错误：{e.Exception.GetBaseException().Message}\n\n" +
            $"当前操作可能未完成，请检查后重试。详细信息已写入日志：\n{logPath}",
            "程序异常", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
