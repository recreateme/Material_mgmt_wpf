namespace MaterialMgmt.Core.Abstractions;

public interface IAuditLog
{
    void Log(string category, string action, string operatorName = "");
    string LogPath { get; }
}
