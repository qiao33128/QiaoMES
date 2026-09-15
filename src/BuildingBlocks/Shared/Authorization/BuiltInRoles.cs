namespace QiaoMES.Shared.Authorization;

/// <summary>
/// 系统内置角色。内置角色不允许删除，避免把系统锁在门外（例如删掉唯一的 admin）。
/// </summary>
public static class BuiltInRoles
{
    public const string Admin = "admin";
    public const string Supervisor = "supervisor";
    public const string Operator = "operator";

    public static IReadOnlyList<string> All { get; } = [Admin, Supervisor, Operator];

    public static bool IsBuiltIn(string roleName) => All.Contains(roleName, StringComparer.OrdinalIgnoreCase);
}
