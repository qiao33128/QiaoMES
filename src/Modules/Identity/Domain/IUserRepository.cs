namespace QiaoMES.Identity.Domain;

/// <summary>
/// 用户仓储接口。
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> IsUsernameTakenAsync(string username, CancellationToken cancellationToken = default);
    void Add(User user);
    void Update(User user);

    /// <summary>提交当前上下文的所有变更。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
