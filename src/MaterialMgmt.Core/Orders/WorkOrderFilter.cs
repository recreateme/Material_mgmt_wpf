namespace MaterialMgmt.Core.Orders;

/// <summary>工单列表筛选条件：状态、开始日期范围（按天，含两端）、配方、文本。空值表示不限。</summary>
public sealed record WorkOrderFilter(
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int? FormulaId = null,
    string? Text = null)
{
    public bool IsEmpty =>
        Status is null && From is null && To is null && FormulaId is null && string.IsNullOrWhiteSpace(Text);

    /// <param name="texts">参与文本匹配的字段（配方名、生产编号、操作员等），任一包含即命中。</param>
    public bool Matches(string status, DateTime startTime, int formulaId, params string?[] texts)
    {
        if (Status is not null && status != Status) return false;
        if (From is { } from && startTime < from.Date) return false;
        if (To is { } to && startTime >= to.Date.AddDays(1)) return false;
        if (FormulaId is { } fid && formulaId != fid) return false;
        if (!string.IsNullOrWhiteSpace(Text))
        {
            var q = Text.Trim();
            if (!texts.Any(t => t?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)) return false;
        }
        return true;
    }
}
