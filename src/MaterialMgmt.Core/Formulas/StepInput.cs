namespace MaterialMgmt.Core.Formulas;

/// <summary>一个配方步骤的可编辑内容（序号另由排序决定）。</summary>
public sealed record StepInput(int MaterialId, double Min, double Max, string? Notes);
