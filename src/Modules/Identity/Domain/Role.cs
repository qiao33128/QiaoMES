using QiaoMES.Shared;

namespace QiaoMES.Identity.Domain;

/// <summary>
/// 系统角色（RBAC）。
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
}
