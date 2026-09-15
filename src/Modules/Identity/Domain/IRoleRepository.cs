namespace QiaoMES.Identity.Domain;

/// <summary>
/// 角色仓储接口。
/// </summary>
public interface IRoleRepository
{
    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> IsNameTakenAsync(string name, Guid? excludeRoleId = null, CancellationToken cancellationToken = default);

    void Add(Role role);
    void Update(Role role);

    /// <summary>
    /// 显式持久化新建立的「角色-权限」关联。
    /// <para>
    /// 必须显式 Add：EF 对「通过导航集合发现、且主键已有值」的实体会判定为 Modified（生成 UPDATE），
    /// 导致 <c>DbUpdateConcurrencyException</c>。
    /// </para>
    /// </summary>
    void AddPermission(RolePermission permission);

    /// <summary>提交当前上下文的所有变更（事务由上层工作单元统一管理）。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
