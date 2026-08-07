namespace QiaoMES.Identity.Domain;

/// <summary>
/// 角色仓储接口。
/// </summary>
public interface IRoleRepository
{
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(Role role);
}
