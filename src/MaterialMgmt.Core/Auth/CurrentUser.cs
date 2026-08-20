namespace MaterialMgmt.Core.Auth;

public sealed class CurrentUser
{
    public int Id { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Role { get; init; } = "";

    public bool IsAdmin() => Role is Roles.Admin or Roles.Root;
    public bool IsOperator() => Role is Roles.Operator or Roles.Root;
    public bool IsRoot() => Role == Roles.Root;
}

public static class Roles
{
    public const string Root = "root";
    public const string Admin = "admin";
    public const string Operator = "operator";
}
