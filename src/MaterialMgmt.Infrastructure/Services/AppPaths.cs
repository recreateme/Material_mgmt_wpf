using MaterialMgmt.Core.Abstractions;

namespace MaterialMgmt.Infrastructure.Services;

public sealed class AppPaths : IAppPaths
{
    public AppPaths(string? appRoot = null)
    {
        AppRoot = string.IsNullOrWhiteSpace(appRoot)
            ? FindAppRoot()
            : Path.GetFullPath(appRoot);
        DataDir = Path.Combine(AppRoot, "data");
        ConfigDir = Path.Combine(AppRoot, "config");
        LogsDir = Path.Combine(AppRoot, "logs");
        DatabasePath = Path.Combine(DataDir, "material_mgmt.db");
        SettingsIniPath = Path.Combine(ConfigDir, "settings.ini");
        AppLogPath = Path.Combine(LogsDir, "app.log");
        AuditLogPath = Path.Combine(LogsDir, "audit.log");
    }

    public string AppRoot { get; }
    public string DataDir { get; }
    public string ConfigDir { get; }
    public string LogsDir { get; }
    public string DatabasePath { get; }
    public string SettingsIniPath { get; }
    public string AppLogPath { get; }
    public string AuditLogPath { get; }

    private static string FindAppRoot()
    {
        // 优先：exe 同级存在 data/ 或 config/ → 发布布局
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Directory.Exists(Path.Combine(baseDir, "data")) ||
            Directory.Exists(Path.Combine(baseDir, "config")))
            return baseDir;

        // 开发：从 bin/Debug/net8.0-windows 向上找到含 WPF重构计划.md 或 src 的仓库根
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WPF重构计划.md")) ||
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                // 若当前是 src，再上一级
                if (dir.Name.Equals("src", StringComparison.OrdinalIgnoreCase) && dir.Parent != null)
                    return dir.Parent.FullName;
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return baseDir;
    }
}
