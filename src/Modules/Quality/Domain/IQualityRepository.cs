namespace QiaoMES.Quality.Domain;

/// <summary>检验单查询条件。</summary>
public sealed record InspectionQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public InspectionType? Type { get; init; }

    public InspectionStatus? Status { get; init; }

    /// <summary>关键字：检验单号 / SN / 产品编码 / 物料编码。</summary>
    public string? Keyword { get; init; }

    public Guid? WorkOrderId { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>不合格处置单查询条件。</summary>
public sealed record NonconformanceQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public DispositionStatus? Status { get; init; }

    public DispositionType? Disposition { get; init; }

    /// <summary>关键字：处置单号 / SN / 不良代码。</summary>
    public string? Keyword { get; init; }

    public Guid? WorkOrderId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>不良代码查询条件。</summary>
public sealed record DefectCodeQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public string? Keyword { get; init; }

    public string? Category { get; init; }

    public bool? IsActive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>SPC 数据点（某检验项的一次实测）。</summary>
public sealed record SpcSample(DateTime Timestamp, decimal? Value, bool? IsQualified, string InspectionNumber);

/// <summary>
/// 检验单仓储。
/// </summary>
public interface IInspectionRepository
{
    /// <summary>取某检验项的历史数据点（SPC 趋势用）。</summary>
    Task<IReadOnlyList<SpcSample>> GetItemHistoryAsync(
        string itemName,
        DateTime? from,
        DateTime? to,
        int points,
        CancellationToken cancellationToken = default);

    Task<Inspection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Inspection?> GetByNumberAsync(string inspectionNumber, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Inspection> Items, int TotalCount)> QueryAsync(
        InspectionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>生成下一个检验单号（按类型 + 日期，数据库原子分配）。</summary>
    Task<string> NextNumberAsync(InspectionType type, DateTime date, CancellationToken cancellationToken = default);

    void Add(Inspection inspection);

    /// <summary>显式持久化新加的检验项（EF 不会把导航集合中的新实体判为新增）。</summary>
    void AddItem(InspectionItem item);

    /// <summary>按 SN 取检验历史（追溯用）。</summary>
    Task<IReadOnlyList<Inspection>> GetBySnAsync(string sn, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 不合格处置单仓储。
/// </summary>
public interface INonconformanceRepository
{
    Task<Nonconformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<Nonconformance> Items, int TotalCount)> QueryAsync(
        NonconformanceQuery query,
        CancellationToken cancellationToken = default);

    Task<string> NextNumberAsync(DateTime date, CancellationToken cancellationToken = default);

    /// <summary>统计某检验单已产生的处置单数量。</summary>
    Task<int> CountByInspectionAsync(Guid inspectionId, CancellationToken cancellationToken = default);

    void Add(Nonconformance nonconformance);

    /// <summary>显式持久化新加的维修记录。</summary>
    void AddRepair(RepairRecord repair);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 不良代码仓储。
/// </summary>
public interface IDefectCodeRepository
{
    Task<DefectCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DefectCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> IsCodeTakenAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DefectCode> Items, int TotalCount)> QueryAsync(
        DefectCodeQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>按不良代码统计出现次数（Pareto 用）。</summary>
    Task<IReadOnlyList<(string DefectCode, int Count)>> TopDefectsAsync(
        DateTime from,
        DateTime to,
        int top,
        CancellationToken cancellationToken = default);

    void Add(DefectCode defectCode);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
