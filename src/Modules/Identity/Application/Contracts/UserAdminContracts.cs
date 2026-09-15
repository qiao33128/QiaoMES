namespace QiaoMES.Identity.Application.Contracts;

/// <summary>管理员视角的用户信息。</summary>
public record AdminUserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? Email,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> Roles);

/// <summary>由管理员创建用户。</summary>
public record CreateUserRequest(
    string Username,
    string Password,
    string DisplayName,
    string? Email,
    IReadOnlyList<Guid>? RoleIds);

/// <summary>整体替换用户角色。</summary>
public record UpdateUserRolesRequest(IReadOnlyList<Guid> RoleIds);

/// <summary>启用 / 停用用户。</summary>
public record SetUserActiveRequest(bool IsActive);
