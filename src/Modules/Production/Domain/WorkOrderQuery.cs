namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单查询条件。
/// <para>
/// 由领域层描述查询意图，具体 SQL 由仓储实现下推到数据库，
/// 避免「取出全表再在内存里筛选」。
/// </para>
/// </summary>
public sealed record WorkOrderQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public WorkOrderStatus? Status { get; init; }
    public string? Keyword { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>规范化后的页码（从 1 开始）。</summary>
    public int NormalizedPage => Page < 1 ? 1 : Page;

    /// <summary>规范化后的页大小（1 ~ 100）。</summary>
    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    /// <summary>需要跳过的行数，直接用于 SQL OFFSET。</summary>
    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
