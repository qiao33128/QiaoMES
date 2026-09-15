using QiaoMES.Shared;

namespace QiaoMES.Identity.Domain;

/// <summary>
/// 角色-权限关联。权限目录定义在 <see cref="QiaoMES.Shared.Authorization.Permissions"/>。
/// </summary>
public class RolePermission : Entity
{
    private RolePermission() { }

    public RolePermission(Guid roleId, string permission)
    {
        RoleId = roleId;
        Permission = permission;
    }

    public Guid RoleId { get; private set; }

    /// <summary>权限标识，如 <c>workorders:create</c>。</summary>
    public string Permission { get; private set; } = string.Empty;
}
