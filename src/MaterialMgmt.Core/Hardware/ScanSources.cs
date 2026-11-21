namespace MaterialMgmt.Core.Hardware;

/// <summary>称重记录里的扫码来源取值（weight_records.scan_source）。</summary>
public static class ScanSources
{
    public const string Serial = "serial";
    public const string Hid = "hid";
    public const string Simulated = "simulated";

    public static string ToDisplay(string? source) => source switch
    {
        Serial => "串口扫码枪",
        Hid => "键盘模式扫码枪",
        Simulated => "模拟输入",
        _ => ""
    };
}
