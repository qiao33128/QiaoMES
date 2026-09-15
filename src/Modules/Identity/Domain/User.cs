using QiaoMES.Shared;

namespace QiaoMES.Identity.Domain;

/// <summary>
/// 系统用户。
/// </summary>
public class User : Entity
{
    private User() { }

    public User(string username, string passwordHash, string displayName, string? email = null)
        : base(Guid.NewGuid())
    {
        Username = username;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Email = email;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public string Username { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? LastLoginAt { get; private set; }

    private readonly List<UserRole> _roles = [];
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    /// <summary>当前生效的角色 Id。</summary>
    public IReadOnlyList<Guid> RoleIds => _roles
        .Where(r => !r.IsDeleted)
        .Select(r => r.RoleId)
        .Distinct()
        .ToList();

    public UserRole? AddRole(Role role) => AddRole(role.Id);

    /// <summary>
    /// 授予角色（幂等；已软删除的关联会被恢复）。
    /// </summary>
    /// <returns>
    /// 新创建的关联；调用方**必须**把它交给仓储显式持久化。
    /// 已存在或已恢复的关联返回 <c>null</c>。
    /// </returns>
    public UserRole? AddRole(Guid roleId)
    {
        if (_roles.Any(r => r.RoleId == roleId && !r.IsDeleted))
        {
            return null;
        }

        var removed = _roles.FirstOrDefault(r => r.RoleId == roleId && r.IsDeleted);
        if (removed is not null)
        {
            removed.Restore();
            return null;
        }

        var created = new UserRole(Id, roleId);
        _roles.Add(created);
        return created;
    }

    public void RemoveRole(Guid roleId)
    {
        var existing = _roles.FirstOrDefault(r => r.RoleId == roleId && !r.IsDeleted);
        existing?.Delete();
    }

    /// <summary>
    /// 用给定集合整体替换角色（多余移除、缺失补上）。
    /// </summary>
    /// <returns>新增的关联列表，调用方需显式持久化。</returns>
    public IReadOnlyList<UserRole> ReplaceRoles(IEnumerable<Guid> roleIds)
    {
        var target = roleIds.Distinct().ToHashSet();

        foreach (var roleId in RoleIds.Where(id => !target.Contains(id)))
        {
            RemoveRole(roleId);
        }

        var added = new List<UserRole>();
        foreach (var roleId in target)
        {
            var created = AddRole(roleId);
            if (created is not null)
            {
                added.Add(created);
            }
        }

        return added;
    }

    public void UpdatePassword(string passwordHash) => PasswordHash = passwordHash;

    public void UpdateProfile(string displayName, string? email)
    {
        DisplayName = displayName;
        Email = email;
    }

    public void SetActive(bool active) => IsActive = active;

    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;
}
