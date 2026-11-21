using System.Globalization;

namespace MaterialMgmt.Core.Formulas;

/// <summary>配方步骤上下限校验（单位 g）：都必须大于 0，且下限不大于上限。</summary>
public static class StepRange
{
    /// <summary>返回 null 表示合法；否则返回给用户看的错误说明。</summary>
    public static string? Validate(string? minText, string? maxText, out double min, out double max)
    {
        max = 0;
        if (!TryParse(minText, out min)) return "下限请输入数字";
        if (!TryParse(maxText, out max)) return "上限请输入数字";
        if (min <= 0) return "下限必须大于 0";
        if (max <= 0) return "上限必须大于 0";
        if (min > max) return "下限不能大于上限";
        return null;
    }

    private static bool TryParse(string? text, out double value)
    {
        value = 0;
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return false;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
