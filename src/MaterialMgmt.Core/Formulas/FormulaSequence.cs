namespace MaterialMgmt.Core.Formulas;

/// <summary>
/// 为 (formula_id, sequence) 唯一索引生成两阶段序号，避免交换时中间态冲突。
/// </summary>
public static class FormulaSequence
{
    public const int TempOffset = 10_000;

    public static IReadOnlyList<(int Id, int Sequence)> BuildTwoPhase(IReadOnlyList<int> orderedIds)
    {
        var result = new List<(int, int)>(orderedIds.Count * 2);
        for (var i = 0; i < orderedIds.Count; i++)
            result.Add((orderedIds[i], TempOffset + i + 1));
        for (var i = 0; i < orderedIds.Count; i++)
            result.Add((orderedIds[i], i + 1));
        return result;
    }

    /// <summary>
    /// 把 <paramref name="fromIndex"/> 处的项移到间隙 <paramref name="gapIndex"/>（0 = 最前，Count = 最后）。
    /// 间隙按移动前的列表计算，越界自动修正。
    /// </summary>
    public static List<int> MoveTo(IReadOnlyList<int> orderedIds, int fromIndex, int gapIndex)
    {
        var result = orderedIds.ToList();
        if (fromIndex < 0 || fromIndex >= result.Count) return result;
        gapIndex = Math.Clamp(gapIndex, 0, result.Count);
        var id = result[fromIndex];
        result.RemoveAt(fromIndex);
        result.Insert(gapIndex > fromIndex ? gapIndex - 1 : gapIndex, id);
        return result;
    }
}
