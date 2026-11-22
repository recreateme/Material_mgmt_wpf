namespace MaterialMgmt.Tests;

/// <summary>
/// 不接电子秤、不接扫码枪、不打开 data/material_mgmt.db 的检验。
/// 服务测试继承 TestHost，每个测试一个临时目录。改规则时先改同名测试，再改服务。
/// </summary>
internal static class NamespaceDoc
{
    public const string Purpose = "自动化测试。只用临时库。";
}
