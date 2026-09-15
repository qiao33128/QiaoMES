namespace QiaoMES.Reporting.Domain;

/// <summary>班次查询条件。</summary>
public sealed record ShiftQuery
{
    public bool? IsActive { get; init; }

    public string? LineName { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > 200 ? 50 : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>
/// 班次定义仓储。
/// </summary>
public interface IShiftRepository
{
    Task<ShiftDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ShiftDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> IsCodeTakenAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>取启用中的班次（指定产线的优先，无匹配时回落到全局班次）。</summary>
    Task<IReadOnlyList<ShiftDefinition>> GetActiveAsync(string? lineName, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ShiftDefinition> Items, int TotalCount)> QueryAsync(
        ShiftQuery query,
        CancellationToken cancellationToken = default);

    void Add(ShiftDefinition shift);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 生产日历仓储。
/// </summary>
public interface ICalendarRepository
{
    Task<CalendarDay?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarDay>> GetRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CalendarDay> Items, int TotalCount)> QueryAsync(
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    void Add(CalendarDay day);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
