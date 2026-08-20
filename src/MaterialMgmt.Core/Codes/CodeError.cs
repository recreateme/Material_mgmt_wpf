namespace MaterialMgmt.Core.Codes;

public enum CodeKind
{
    Material,
    Formula
}

public sealed class CodeError : Exception
{
    public CodeError(string message) : base(message) { }
}
