namespace QiaoMES.Production.Domain;

/// <summary>SN 查询条件。</summary>
public sealed record SerialNumberQuery
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public Guid? WorkOrderId { get; init; }

    public SerialNumberStatus? Status { get; init; }

    /// <summary>SN 关键字（前缀匹配）。</summary>
    public string? Keyword { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int NormalizedPage => Page < 1 ? 1 : Page;

    public int NormalizedPageSize => PageSize is < 1 or > MaxPageSize ? DefaultPageSize : PageSize;

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>
/// 序列号与过站记录仓储。
/// </summary>
public interface ISerialNumberRepository
{
    Task<SerialNumber?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按 SN 取详情（含过站轨迹）。</summary>
    Task<SerialNumber?> GetBySnAsync(string sn, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string sn, CancellationToken cancellationToken = default);

    /// <summary>该工单已生成的 SN 数量（用于生成下一个序号）。</summary>
    Task<int> CountByWorkOrderAsync(Guid workOrderId, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SerialNumber> Items, int TotalCount)> QueryAsync(
        SerialNumberQuery query,
        CancellationToken cancellationToken = default);

    void Add(SerialNumber serialNumber);

    /// <summary>显式持久化过站记录（EF 不会把导航集合中的新实体判为新增）。</summary>
    void AddTracking(WipTracking tracking);

    /// <summary>按状态统计 SN 数量（指标统计用；from / to 按创建时间过滤）。</summary>
    Task<IReadOnlyList<(SerialNumberStatus Status, int Count)>> CountByStatusAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
