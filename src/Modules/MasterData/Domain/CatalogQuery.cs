namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 主数据通用查询条件（筛选与分页全部下推到数据库）。
/// </summary>
public sealed record CatalogQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    /// <summary>关键字：匹配编码或名称。</summary>
    public string? Keyword { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
