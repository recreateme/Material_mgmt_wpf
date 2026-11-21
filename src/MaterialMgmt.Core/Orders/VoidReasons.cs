namespace MaterialMgmt.Core.Orders;

/// <summary>重称作废原因的固定选项；“其他”须附说明。</summary>
public static class VoidReasons
{
    public const string ReadingAbnormal = "读数异常";
    public const string WrongMaterial = "放错料";
    public const string WrongAmount = "加料量需调整";
    public const string Other = "其他";
    public const string SystemRepair = "系统修复";

    /// <summary>操作员可选的原因（不含系统修复）。</summary>
    public static readonly IReadOnlyList<string> Selectable = new[] { ReadingAbnormal, WrongMaterial, WrongAmount, Other };

    /// <summary>组合为落库文本：“原因”或“原因：说明”，说明为空时只存原因。</summary>
    public static string Compose(string reason, string? note)
    {
        var n = note?.Trim();
        return string.IsNullOrEmpty(n) ? reason : $"{reason}：{n}";
    }
}
