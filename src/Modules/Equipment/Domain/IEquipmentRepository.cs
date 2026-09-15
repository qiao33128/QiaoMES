namespace QiaoMES.Equipment.Domain;

/// <summary>设备查询条件。</summary>
public sealed record EquipmentQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public string? Keyword { get; init; }

    public EquipmentStatus? Status { get; init; }

    public string? LineName { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>Andon 呼叫查询条件。</summary>
public sealed record AndonQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public AndonStatus? Status { get; init; }

    public AndonType? Type { get; init; }

    public AndonLevel? Level { get; init; }

    /// <summary>仅看未结束的呼叫（看板用）。</summary>
    public bool? OnlyOpen { get; init; }

    public string? Keyword { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>停机 Pareto 项（按原因代码聚合）。</summary>
public sealed record DowntimeParetoItem(string ReasonCode, int Count);

/// <summary>
/// 设备仓储。
/// </summary>
public interface IEquipmentRepository
{
    Task<Equipment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Equipment?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> IsCodeTakenAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Equipment> Items, int TotalCount)> QueryAsync(
        EquipmentQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>按状态统计设备数量（看板红黄绿）。</summary>
    Task<IReadOnlyList<(EquipmentStatus Status, int Count)>> CountByStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>停机原因 Pareto（按状态变更记录聚合）。</summary>
    Task<IReadOnlyList<DowntimeParetoItem>> DowntimeParetoAsync(
        DateTime from,
        DateTime to,
        int top,
        CancellationToken cancellationToken = default);

    void Add(Equipment equipment);

    /// <summary>显式持久化状态变更记录（EF 不会把导航集合中的新实体判为新增）。</summary>
    void AddStatusLog(EquipmentStatusLog log);

    /// <summary>显式持久化点检 / 保养记录。</summary>
    void AddMaintenanceRecord(EquipmentMaintenanceRecord record);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Andon 呼叫仓储。
/// </summary>
public interface IAndonRepository
{
    Task<AndonCall?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AndonCall> Items, int TotalCount)> QueryAsync(
        AndonQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>取超时未响应的呼叫（后台升级任务使用）。</summary>
    Task<IReadOnlyList<AndonCall>> GetTimeoutCallsAsync(DateTime now, CancellationToken cancellationToken = default);

    Task<string> NextNumberAsync(DateTime date, CancellationToken cancellationToken = default);

    void Add(AndonCall call);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
