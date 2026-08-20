using System.Text.RegularExpressions;
using MaterialMgmt.Core.Entities;

namespace MaterialMgmt.Core.Codes;

/// <summary>业务唯一码生成与校验（对齐 Python code_service）。</summary>
public static class CodeService
{
    private static readonly Regex CodeRe = new(@"^[A-Z0-9][A-Z0-9\-_.]{2,63}$", RegexOptions.Compiled);

    public static string Prefix(CodeKind kind) => kind switch
    {
        CodeKind.Material => "MAT-",
        CodeKind.Formula => "FML-",
        _ => throw new CodeError($"未知编码类型：{kind}")
    };

    public static string Normalize(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static void ValidateFormat(string code)
    {
        var c = Normalize(code);
        if (string.IsNullOrEmpty(c)) throw new CodeError("编码不能为空");
        if (c.Length > 64) throw new CodeError("编码最长 64 个字符");
        if (!CodeRe.IsMatch(c)) throw new CodeError("编码仅允许字母、数字、-、_、.，且至少 3 位");
    }

    public static string FormatAutoCode(CodeKind kind, int entityId)
        => $"{Prefix(kind)}{entityId:D6}";

    public static string AssertUnique(
        IEnumerable<string> existingCodes,
        string code,
        int? excludeId = null,
        IEnumerable<(int Id, string Code)>? idCodePairs = null)
    {
        code = Normalize(code);
        ValidateFormat(code);

        if (idCodePairs != null)
        {
            foreach (var (id, existing) in idCodePairs)
            {
                if (excludeId.HasValue && id == excludeId.Value) continue;
                if (string.Equals(existing, code, StringComparison.OrdinalIgnoreCase))
                    throw new CodeError($"编码已存在：{code}");
            }
            return code;
        }

        if (existingCodes.Any(c => string.Equals(Normalize(c), code, StringComparison.OrdinalIgnoreCase)))
            throw new CodeError($"编码已存在：{code}");
        return code;
    }

    public static string AssignAuto(
        CodeKind kind,
        int entityId,
        Func<string, int?, bool> isUnique)
    {
        var code = FormatAutoCode(kind, entityId);
        if (isUnique(code, entityId)) return code;

        var n = entityId;
        while (true)
        {
            n++;
            code = FormatAutoCode(kind, n);
            if (isUnique(code, entityId)) return code;
        }
    }

    public static string PreviewNext(CodeKind kind, int? lastId)
    {
        var nid = (lastId ?? 0) + 1;
        return FormatAutoCode(kind, nid);
    }

    public static bool IsMaterialCode(string code)
        => Normalize(code).StartsWith("MAT-", StringComparison.Ordinal);

    public static bool IsFormulaCode(string code)
        => Normalize(code).StartsWith("FML-", StringComparison.Ordinal);
}
