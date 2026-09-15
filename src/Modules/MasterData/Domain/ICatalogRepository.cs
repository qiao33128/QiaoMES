namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 主数据仓储。
/// <para>
/// 产品 / 物料 / 工序 / 工作中心 共享同一套「编码唯一 + 关键字分页 + 启停」的读写逻辑，
/// 因此用泛型方法统一表达，避免为每种主数据重复实现一遍。
/// </para>
/// </summary>
public interface ICatalogRepository
{
    Task<TEntity?> GetByIdAsync<TEntity>(Guid id, CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity;

    /// <summary>按业务编码查询（导入时的 upsert 匹配键）。</summary>
    Task<TEntity?> GetByCodeAsync<TEntity>(string code, CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity;

    /// <summary>编码是否已被占用（<paramref name="excludeId"/> 用于更新时排除自身）。</summary>
    Task<bool> IsCodeTakenAsync<TEntity>(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity;

    /// <summary>按条件分页查询，返回当前页数据与总数。</summary>
    Task<(IReadOnlyList<TEntity> Items, int TotalCount)> QueryPagedAsync<TEntity>(
        CatalogQuery query,
        CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity;

    /// <summary>批量按 Id 查询（用于校验引用是否存在、是否启用）。</summary>
    Task<IReadOnlyList<TEntity>> GetByIdsAsync<TEntity>(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity;

    void Add<TEntity>(TEntity entity) where TEntity : CatalogEntity;

    /// <summary>提交当前上下文的所有变更（事务由上层工作单元统一管理）。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
