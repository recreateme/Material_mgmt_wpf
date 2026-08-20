namespace MaterialMgmt.Core.Abstractions;

public interface IAppPaths
{
    string AppRoot { get; }
    string DataDir { get; }
    string ConfigDir { get; }
    string LogsDir { get; }
    string DatabasePath { get; }
    string SettingsIniPath { get; }
    string AppLogPath { get; }
    string AuditLogPath { get; }
}
