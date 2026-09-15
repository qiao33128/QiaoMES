namespace QiaoMES.MasterData.Domain;

/// <summary>BOM 查询条件。</summary>
public sealed record BomQuery
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
/// BOM 仓储（含明细行）。
/// </summary>
public interface IBomRepository
{
    Task<Bom?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>取某产品的生效版本（工单下达时使用）。</summary>
    Task<Bom?> GetActiveByProductAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Bom> Items, int TotalCount)> QueryAsync(BomQuery query, CancellationToken cancellationToken = default);

    Task<bool> IsVersionTakenAsync(Guid productId, string version, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>把该产品下除 <paramref name="keepId"/> 以外的版本全部取消生效。</summary>
    Task DeactivateOtherVersionsAsync(Guid productId, Guid keepId, CancellationToken cancellationToken = default);

    void Add(Bom bom);

    /// <summary>
    /// 显式持久化新加的明细行。
    /// <para>原因同 <see cref="ICatalogRepository"/> 所在模块的约定：EF 会把「通过导航集合发现、主键已有值」的实体判为 Modified。</para>
    /// </summary>
    void AddItem(BomItem item);

    /// <summary>删除 BOM（生效版本不允许删除，由应用层校验）。</summary>
    void Remove(Bom bom);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
