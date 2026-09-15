namespace QiaoMES.MasterData.Domain;

/// <summary>工艺路线查询条件。</summary>
public sealed record RoutingQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public Guid? ProductId { get; init; }

    public bool? IsActive { get; init; }

    /// <summary>版本号关键字。</summary>
    public string? Keyword { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>
/// 工艺路线仓储（含工序步骤）。
/// </summary>
public interface IRoutingRepository
{
    Task<Routing?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>取某产品的生效版本（工单下达时用于展开工序）。</summary>
    Task<Routing?> GetActiveByProductAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Routing> Items, int TotalCount)> QueryAsync(RoutingQuery query, CancellationToken cancellationToken = default);

    Task<bool> IsVersionTakenAsync(Guid productId, string version, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>把该产品下除 <paramref name="keepId"/> 以外的版本全部取消生效。</summary>
    Task DeactivateOtherVersionsAsync(Guid productId, Guid keepId, CancellationToken cancellationToken = default);

    void Add(Routing routing);

    /// <summary>显式持久化新加的工序步骤（原因同 BOM 明细）。</summary>
    void AddStep(RoutingStep step);

    /// <summary>删除工艺路线（生效版本不允许删除，由应用层校验）。</summary>
    void Remove(Routing routing);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
