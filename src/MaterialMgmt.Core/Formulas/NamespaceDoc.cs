namespace MaterialMgmt.Core.Formulas;

/// <summary>
/// 配方步骤的纯计算：上下限是否合法、调整顺序时如何避免序号冲突、复制步骤时的字段拷贝。
/// 步骤的增删改在 FormulaService。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "配方步骤的计算，不写库。";
}
