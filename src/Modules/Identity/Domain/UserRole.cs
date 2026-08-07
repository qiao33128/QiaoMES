using QiaoMES.Shared;

namespace QiaoMES.Identity.Domain;

/// <summary>
/// 用户-角色关联。
/// </summary>
public class UserRole : Entity
{
    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
}
