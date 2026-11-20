using System.Globalization;
using System.Text;
using MaterialMgmt.Core.Abstractions;
using MaterialMgmt.Core.Hardware;

namespace MaterialMgmt.Infrastructure.Services;

/// <summary>兼容 Python configparser 的 settings.ini。</summary>
public sealed class ConfigService : IConfigService
{
    private readonly string _path;
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public ConfigService(IAppPaths paths)
    {
        _path = paths.SettingsIniPath;
        LoadDefaults();
        if (File.Exists(_path)) ReadFile();
        else Save();
    }

    public string SerialPort
    {
        get => Get("serial", "port", "");
        set { Set("serial", "port", value); Save(); }
    }

    public int SerialBaud => int.TryParse(Get("serial", "baud_rate", "9600"), out var v) ? v : 9600;

    public bool AutoConnect
    {
        get => bool.TryParse(Get("serial", "auto_connect", "true"), out var v) && v;
        set { Set("serial", "auto_connect", value ? "true" : "false"); Save(); }
    }

    public string ScannerPort
    {
        get => Get("scanner", "port", "");
        set { Set("scanner", "port", value); Save(); }
    }

    public int ScannerBaud
    {
        get => int.TryParse(Get("scanner", "baud_rate", "9600"), out var v) ? v : 9600;
        set
        {
            Set("scanner", "baud_rate", value.ToString(CultureInfo.InvariantCulture));
            Save();
        }
    }

    public ScannerInputMode ScannerMode
    {
        get => Enum.TryParse<ScannerInputMode>(Get("scanner", "mode", "Auto"), true, out var m)
            ? m
            : ScannerInputMode.Auto;
        set
        {
            Set("scanner", "mode", value.ToString());
            Save();
        }
    }

    public bool ScannerEnabled
    {
        get => !bool.TryParse(Get("scanner", "enabled", "true"), out var v) || v;
        set
        {
            Set("scanner", "enabled", value ? "true" : "false");
            Save();
        }
    }

    public (int Width, int Height) WindowSize
    {
        get
        {
            var w = int.TryParse(Get("app", "window_width", "1280"), out var wv) ? wv : 1280;
            var h = int.TryParse(Get("app", "window_height", "800"), out var hv) ? hv : 800;
            return (w, h);
        }
    }

    public bool WindowMaximized =>
        bool.TryParse(Get("app", "window_maximized", "false"), out var v) && v;

    public void SaveWindowSize(int width, int height, bool maximized)
    {
        Set("app", "window_width", width.ToString(CultureInfo.InvariantCulture));
        Set("app", "window_height", height.ToString(CultureInfo.InvariantCulture));
        Set("app", "window_maximized", maximized ? "true" : "false");
        Save();
    }

    public void Reload()
    {
        _sections.Clear();
        LoadDefaults();
        if (File.Exists(_path)) ReadFile();
    }

    private void LoadDefaults()
    {
        Set("serial", "port", "");
        Set("serial", "baud_rate", "9600");
        Set("serial", "auto_connect", "true");
        Set("scanner", "port", "");
        Set("scanner", "baud_rate", "9600");
        Set("scanner", "mode", "Auto");
        Set("scanner", "enabled", "true");
        Set("app", "window_width", "1280");
        Set("app", "window_height", "800");
        Set("app", "window_maximized", "false");
    }

    private string Get(string section, string key, string fallback)
        => _sections.TryGetValue(section, out var kv) && kv.TryGetValue(key, out var v) ? v : fallback;

    private void Set(string section, string key, string value)
    {
        if (!_sections.TryGetValue(section, out var kv))
        {
            kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[section] = kv;
        }
        kv[key] = value;
    }

    private void ReadFile()
    {
        string? current = null;
        foreach (var raw in File.ReadAllLines(_path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                if (!_sections.ContainsKey(current))
                    _sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }
            if (current is null) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim();
            _sections[current][key] = val;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var sb = new StringBuilder();
        foreach (var section in _sections)
        {
            sb.Append('[').Append(section.Key).AppendLine("]");
            foreach (var kv in section.Value)
                sb.Append(kv.Key).Append(" = ").AppendLine(kv.Value);
            sb.AppendLine();
        }
        // 先写临时文件再替换，避免写入中途崩溃/断电导致 settings.ini 被截断
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }
}
