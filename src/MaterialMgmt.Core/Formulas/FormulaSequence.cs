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
}
