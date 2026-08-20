using MaterialMgmt.Core.Abstractions;

namespace MaterialMgmt.Infrastructure.Services;

public sealed class AuditLogService : IAuditLog
{
    private readonly object _lock = new();
    private readonly string _path;

    public AuditLogService(IAppPaths paths)
    {
        _path = paths.AuditLogPath;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public string LogPath => _path;

    public void Log(string category, string action, string operatorName = "")
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [INFO] [{category}] {action}" +
                   (string.IsNullOrEmpty(operatorName) ? "" : $"  操作人={operatorName}");
        lock (_lock)
        {
            File.AppendAllText(_path, line + Environment.NewLine);
        }
    }
}
