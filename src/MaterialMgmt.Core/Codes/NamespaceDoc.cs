namespace MaterialMgmt.Core.Codes;

/// <summary>
/// 原料编码 MAT-、配方编码 FML- 的生成与校验。格式不对或重复时抛 CodeError，而不是普通的 InvalidOperationException。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "MAT- / FML- 编码。";
}
