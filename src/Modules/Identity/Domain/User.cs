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

    public void AddRole(Role role) => _roles.Add(new UserRole(Id, role.Id));

    public void UpdatePassword(string passwordHash) => PasswordHash = passwordHash;

    public void UpdateProfile(string displayName, string? email)
    {
        DisplayName = displayName;
        Email = email;
    }

    public void SetActive(bool active) => IsActive = active;

    public void RecordLogin() => LastLoginAt = DateTime.UtcNow;
}
