namespace MaterialMgmt.Core.Formulas;

/// <summary>复制配方时的命名与步骤快照（不含持久化）。</summary>
public static class FormulaClone
{
    public const string CopySuffix = " - 副本";

    public static string DefaultCopyName(string originalName)
    {
        var name = (originalName ?? "").Trim();
        if (string.IsNullOrEmpty(name)) name = "未命名配方";
        if (name.EndsWith(CopySuffix, StringComparison.Ordinal))
            return name;
        return name + CopySuffix;
    }

    public sealed record StepSnap(int MaterialId, int Sequence, double WeightMin, double WeightMax, string? Notes);

    public static IReadOnlyList<StepSnap> SnapshotSteps(
        IEnumerable<(int MaterialId, int Sequence, double WeightMin, double WeightMax, string? Notes)> items)
        => items
            .OrderBy(i => i.Sequence)
            .Select(i => new StepSnap(i.MaterialId, i.Sequence, i.WeightMin, i.WeightMax, i.Notes))
            .ToList();
}
