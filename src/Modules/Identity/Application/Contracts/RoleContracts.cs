namespace QiaoMES.Identity.Application.Contracts;

/// <summary>创建角色。</summary>
public record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string>? Permissions);

/// <summary>修改角色基本信息。</summary>
public record UpdateRoleRequest(string Name, string? Description);

/// <summary>整体替换角色权限。</summary>
public record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions);

/// <summary>角色信息。</summary>
public record RoleDto(
    Guid Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    IReadOnlyList<string> Permissions);

/// <summary>权限项（用于角色管理界面勾选）。</summary>
public record PermissionItemDto(string Code, string Name);

/// <summary>权限分组。</summary>
public record PermissionGroupDto(string Group, IReadOnlyList<PermissionItemDto> Items);
