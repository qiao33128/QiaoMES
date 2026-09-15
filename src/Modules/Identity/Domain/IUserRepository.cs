namespace QiaoMES.Identity.Domain;

/// <summary>
/// 用户仓储接口。
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>按条件分页查询（筛选、排序、分页全部下推到数据库）。</summary>
    Task<(IReadOnlyList<User> Items, int TotalCount)> QueryAsync(
        UserQuery query,
        CancellationToken cancellationToken = default);

    Task<bool> IsUsernameTakenAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>查询拥有指定角色的用户 Id（角色权限变更后用于失效相关用户缓存）。</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsByRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    void Add(User user);
    void Update(User user);

    /// <summary>显式持久化新建立的「用户-角色」关联（原因见 <see cref="IRoleRepository.AddPermission"/>）。</summary>
    void AddRoleLink(UserRole userRole);

    /// <summary>提交当前上下文的所有变更（事务由上层工作单元统一管理）。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
