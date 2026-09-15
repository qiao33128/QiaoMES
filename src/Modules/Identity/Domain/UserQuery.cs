namespace QiaoMES.Identity.Domain;

/// <summary>
/// 用户查询条件（查询意图由领域层描述，SQL 由仓储下推到数据库）。
/// </summary>
public sealed record UserQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public string? Keyword { get; init; }
    public bool? IsActive { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}
