using System.IO;
using System.Reflection;

namespace MaterialMgmt.App.Services;

/// <summary>应用版本信息，与 csproj &lt;Version&gt; 同步。</summary>
public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    public static string DisplayVersion => $"v{Version}";

    public static string WindowTitle => $"原料管理系统 {DisplayVersion}";

    public static DateTime? BuildDate { get; } = ReadBuildDate();

    public static string BuildDateDisplay =>
        BuildDate?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    private static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static DateTime? ReadBuildDate()
    {
        var path = asmPath();
        if (string.IsNullOrEmpty(path)) return null;
        try { return File.GetLastWriteTime(path); }
        catch { return null; }

        static string? asmPath()
        {
            try { return Assembly.GetExecutingAssembly().Location; }
            catch { return null; }
        }
    }
}
