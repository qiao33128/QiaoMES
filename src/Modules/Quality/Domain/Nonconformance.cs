using QiaoMES.Shared;

namespace QiaoMES.Quality.Domain;

/// <summary>不合格品处置方式。</summary>
public enum DispositionType
{
    /// <summary>返工（按原工艺重做）。</summary>
    Rework = 0,

    /// <summary>返修（修复缺陷后使用）。</summary>
    Repair = 1,

    /// <summary>让步接收（特许使用）。</summary>
    Concession = 2,

    /// <summary>报废。</summary>
    Scrap = 3,

    /// <summary>退货 / 退回供应商。</summary>
    Return = 4,
}

/// <summary>不合格品处置状态。</summary>
public enum DispositionStatus
{
    /// <summary>待处理（尚未决定处置方式）。</summary>
    Pending = 0,

    /// <summary>处理中（维修 / 返工进行中）。</summary>
    InProgress = 1,

    /// <summary>待复检。</summary>
    PendingReinspect = 2,

    /// <summary>已关闭。</summary>
    Closed = 3,
}

/// <summary>
/// 不合格品处置单（NCR）。检验判定不合格后创建，记录处置方式与维修过程，维修完成后可复检。
/// </summary>
public class Nonconformance : Entity
{
    private Nonconformance() { }

    public Nonconformance(
        string nonconformanceNumber,
        int quantity = 1,
        Guid? inspectionId = null,
        Guid? workOrderId = null,
        string? sn = null,
        string? defectCode = null,
        string? defectDescription = null,
        string? productCode = null)
        : base(Guid.NewGuid())
    {
        NonconformanceNumber = nonconformanceNumber;
        Quantity = quantity;
        InspectionId = inspectionId;
        WorkOrderId = workOrderId;
        Sn = sn?.Trim();
        DefectCode = defectCode?.Trim();
        DefectDescription = defectDescription?.Trim();
        ProductCode = productCode;
        Status = DispositionStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public string NonconformanceNumber { get; private set; } = string.Empty;

    /// <summary>来源检验单。</summary>
    public Guid? InspectionId { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    /// <summary>受影响的 SN（单颗不良时填写）。</summary>
    public string? Sn { get; private set; }

    public string? ProductCode { get; private set; }

    public string? DefectCode { get; private set; }

    public string? DefectDescription { get; private set; }

    /// <summary>不良数量。</summary>
    public int Quantity { get; private set; }

    public DispositionType? Disposition { get; private set; }

    public DispositionStatus Status { get; private set; }

    /// <summary>处置后是否需要复检。</summary>
    public bool NeedReinspect { get; private set; }

    /// <summary>处置人。</summary>
    public Guid? HandlerId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? DecidedAt { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    public string? Remark { get; private set; }

    private readonly List<RepairRecord> _repairs = [];
    public IReadOnlyCollection<RepairRecord> Repairs => _repairs.AsReadOnly();

    public IReadOnlyList<RepairRecord> OrderedRepairs => _repairs.OrderBy(r => r.StartedAt).ToList();

    /// <summary>
    /// 决定处置方式。返工 / 返修进入处理中，让步接收 / 报废 / 退货直接关闭。
    /// </summary>
    public Result Decide(DispositionType disposition, bool needReinspect = true, Guid? handlerId = null, string? remark = null)
    {
        if (Status != DispositionStatus.Pending)
        {
            return Result.Failure(Error.Conflict("Nonconformance.AlreadyDecided", "该处置单已决定处置方式"));
        }

        Disposition = disposition;
        NeedReinspect = needReinspect;
        HandlerId = handlerId;
        Remark = remark?.Trim();
        DecidedAt = DateTime.UtcNow;

        switch (disposition)
        {
            case DispositionType.Rework:
            case DispositionType.Repair:
                Status = DispositionStatus.InProgress;
                break;

            default:
                // 让步接收 / 报废 / 退货：不需要维修，直接关闭
                Status = DispositionStatus.Closed;
                ClosedAt = DateTime.UtcNow;
                break;
        }

        return Result.Success();
    }

    /// <summary>
    /// 开始一次维修 / 返工。
    /// </summary>
    /// <returns>新建的维修记录；聚合已被跟踪时调用方需显式持久化。</returns>
    public Result<RepairRecord> StartRepair(string description, Guid? repairerId = null, string? remark = null)
    {
        if (Status != DispositionStatus.InProgress)
        {
            return Result.Failure<RepairRecord>(Error.Conflict(
                "Nonconformance.NotInProgress",
                "只有处理中的处置单才能登记维修"));
        }

        var record = new RepairRecord(Id, description, repairerId, remark);
        _repairs.Add(record);
        return Result.Success(record);
    }

    /// <summary>完成维修。需复检则转为待复检，否则关闭。</summary>
    public Result CompleteRepair(Guid repairRecordId, string? result = null, string? remark = null)
    {
        if (Status != DispositionStatus.InProgress)
        {
            return Result.Failure(Error.Conflict("Nonconformance.NotInProgress", "只有处理中的处置单才能完成维修"));
        }

        var record = _repairs.FirstOrDefault(r => r.Id == repairRecordId);
        if (record is null)
        {
            return Result.Failure(Error.NotFound("Nonconformance.RepairNotFound", "维修记录不存在"));
        }
        if (record.CompletedAt is not null)
        {
            return Result.Failure(Error.Conflict("Nonconformance.RepairCompleted", "该维修记录已完成"));
        }

        record.Complete(result, remark);

        if (NeedReinspect)
        {
            Status = DispositionStatus.PendingReinspect;
        }
        else
        {
            Status = DispositionStatus.Closed;
            ClosedAt = DateTime.UtcNow;
        }

        return Result.Success();
    }

    /// <summary>复检合格 → 关闭处置单。</summary>
    public Result PassReinspection(Guid? reinspectionId = null, string? remark = null)
    {
        if (Status != DispositionStatus.PendingReinspect)
        {
            return Result.Failure(Error.Conflict("Nonconformance.NotPendingReinspect", "该处置单当前不在待复检状态"));
        }

        ReinspectionId = reinspectionId;
        Status = DispositionStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        Remark = remark?.Trim() ?? Remark;
        return Result.Success();
    }

    /// <summary>复检不合格 → 回到处理中，重新维修。</summary>
    public Result FailReinspection(Guid? reinspectionId = null, string? remark = null)
    {
        if (Status != DispositionStatus.PendingReinspect)
        {
            return Result.Failure(Error.Conflict("Nonconformance.NotPendingReinspect", "该处置单当前不在待复检状态"));
        }

        ReinspectionId = reinspectionId;
        Status = DispositionStatus.InProgress;
        Remark = remark?.Trim() ?? Remark;
        return Result.Success();
    }

    /// <summary>直接改为报废并关闭（维修后仍不合格时的兜底处置）。</summary>
    public Result Scrap(string? remark = null)
    {
        if (Status == DispositionStatus.Closed)
        {
            return Result.Failure(Error.Conflict("Nonconformance.AlreadyClosed", "该处置单已关闭"));
        }

        Disposition = DispositionType.Scrap;
        Status = DispositionStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        Remark = remark?.Trim() ?? Remark;
        return Result.Success();
    }

    /// <summary>最近一次复检单。</summary>
    public Guid? ReinspectionId { get; private set; }

    /// <summary>是否已关闭。</summary>
    public bool IsClosed => Status == DispositionStatus.Closed;
}

/// <summary>
/// 维修 / 返工记录。一条处置单可有多条（多次返修）。
/// </summary>
public class RepairRecord : Entity
{
    private RepairRecord() { }

    public RepairRecord(Guid nonconformanceId, string description, Guid? repairerId = null, string? remark = null)
        : base(Guid.NewGuid())
    {
        NonconformanceId = nonconformanceId;
        Description = description.Trim();
        RepairerId = repairerId;
        Remark = remark?.Trim();
        StartedAt = DateTime.UtcNow;
    }

    public Guid NonconformanceId { get; private set; }

    /// <summary>维修内容。</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>维修结果说明。</summary>
    public string? Result { get; private set; }

    public Guid? RepairerId { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public string? Remark { get; private set; }

    public void Complete(string? result, string? remark)
    {
        Result = result?.Trim();
        Remark = remark?.Trim() ?? Remark;
        CompletedAt = DateTime.UtcNow;
    }
}
