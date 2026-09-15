using QiaoMES.Shared;

namespace QiaoMES.Identity.Domain;

/// <summary>
/// 系统角色（RBAC）。角色是权限的集合，权限本身由代码中的权限目录定义。
/// </summary>
public class Role : Entity
{
    private Role() { }

    public Role(string name, string description = "")
        : base(Guid.NewGuid())
    {
        Name = name;
        Description = description;
    }

    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    private readonly List<UserRole> _users = [];
    public IReadOnlyCollection<UserRole> Users => _users.AsReadOnly();

    private readonly List<RolePermission> _permissions = [];
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    /// <summary>当前生效的权限标识（有序，便于前端展示与比对）。</summary>
    public IReadOnlyList<string> PermissionNames => _permissions
        .Where(p => !p.IsDeleted)
        .Select(p => p.Permission)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToList();

    public void Rename(string name, string description)
    {
        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// 授予权限（幂等；已软删除的同一权限会被恢复）。
    /// </summary>
    /// <returns>
    /// 新创建的关联；调用方**必须**把它交给仓储显式持久化。
    /// 已存在的关联返回 <c>null</c>（无需持久化）。
    /// </returns>
    public RolePermission? GrantPermission(string permission)
    {
        var normalized = permission.Trim();
        var existing = _permissions.FirstOrDefault(p => p.Permission == normalized);

        if (existing is not null)
        {
            if (existing.IsDeleted)
            {
                existing.Restore();
            }

            return null;
        }

        var created = new RolePermission(Id, normalized);
        _permissions.Add(created);
        return created;
    }

    /// <summary>回收权限（幂等）。已加载的关联由 EF 变更跟踪自动软删除。</summary>
    public void RevokePermission(string permission)
    {
        var normalized = permission.Trim();
        var existing = _permissions.FirstOrDefault(p => p.Permission == normalized && !p.IsDeleted);
        existing?.Delete();
    }

    /// <summary>
    /// 用给定集合整体替换权限（多余回收、缺失授予）。
    /// </summary>
    /// <returns>新增的关联列表，调用方需显式持久化。</returns>
    public IReadOnlyList<RolePermission> ReplacePermissions(IEnumerable<string> permissions)
    {
        var target = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var existing in _permissions.Where(p => !p.IsDeleted && !target.Contains(p.Permission)))
        {
            existing.Delete();
        }

        var added = new List<RolePermission>();
        foreach (var permission in target)
        {
            var created = GrantPermission(permission);
            if (created is not null)
            {
                added.Add(created);
            }
        }

        return added;
    }
}
