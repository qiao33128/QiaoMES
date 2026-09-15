using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

/// <summary>工单工序任务的执行状态。</summary>
public enum WorkOrderOperationStatus
{
    /// <summary>待开工。</summary>
    Pending = 0,

    /// <summary>进行中（已有报工）。</summary>
    InProgress = 1,

    /// <summary>已完成（累计报工达到计划数量）。</summary>
    Completed = 2,
}

/// <summary>
/// 工单的一道工序任务。工单下达时按产品的生效工艺路线展开生成。
/// <para>
/// 工序的编码、名称、工作中心、标准工时都是**下达时刻的快照**，
/// 之后主数据变更不会影响在制工单。
/// </para>
/// </summary>
public class WorkOrderOperation : Entity
{
    private WorkOrderOperation() { }

    public WorkOrderOperation(Guid workOrderId, RoutingStepReleaseSnapshot step, int plannedQuantity)
        : base(Guid.NewGuid())
    {
        WorkOrderId = workOrderId;
        Sequence = step.Sequence;
        OperationId = step.OperationId;
        OperationCode = step.OperationCode;
        OperationName = step.OperationName;
        WorkCenterId = step.WorkCenterId;
        StandardSeconds = step.StandardSeconds;
        IsQualityGate = step.IsQualityGate;
        PlannedQuantity = plannedQuantity;
        Status = WorkOrderOperationStatus.Pending;
    }

    public Guid WorkOrderId { get; private set; }

    /// <summary>工序顺序（10、20、30…）。</summary>
    public int Sequence { get; private set; }

    public Guid OperationId { get; private set; }

    /// <summary>工序编码（下达时快照）。</summary>
    public string OperationCode { get; private set; } = string.Empty;

    /// <summary>工序名称（下达时快照）。</summary>
    public string OperationName { get; private set; } = string.Empty;

    public Guid? WorkCenterId { get; private set; }

    /// <summary>标准工时（秒 / 件，下达时快照）。</summary>
    public int StandardSeconds { get; private set; }

    /// <summary>是否为质检点。</summary>
    public bool IsQualityGate { get; private set; }

    public int PlannedQuantity { get; private set; }

    public int GoodQuantity { get; private set; }

    public int DefectQuantity { get; private set; }

    public int ScrapQuantity { get; private set; }

    public WorkOrderOperationStatus Status { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    /// <summary>累计报工数量（良品 + 不良 + 报废）。</summary>
    public int ReportedQuantity => GoodQuantity + DefectQuantity + ScrapQuantity;

    /// <summary>进度百分比（0~100）。</summary>
    public int ProgressPercent => PlannedQuantity <= 0
        ? 0
        : (int)Math.Min(100, Math.Round(ReportedQuantity * 100d / PlannedQuantity));

    /// <summary>
    /// 报工。累计数量达到计划数量时该工序自动完成。
    /// </summary>
    public Result Report(int goodQuantity, int defectQuantity, int scrapQuantity)
    {
        if (goodQuantity < 0 || defectQuantity < 0 || scrapQuantity < 0)
        {
            return Result.Failure(Error.Validation("WorkOrderOperation.InvalidQuantity", "报工数量不能为负数"));
        }

        var total = goodQuantity + defectQuantity + scrapQuantity;
        if (total <= 0)
        {
            return Result.Failure(Error.Validation("WorkOrderOperation.EmptyReport", "报工数量必须大于 0"));
        }

        if (ReportedQuantity + total > PlannedQuantity)
        {
            return Result.Failure(Error.Validation(
                "WorkOrderOperation.OverProduction",
                $"累计报工将超出计划数量（计划 {PlannedQuantity}，已报 {ReportedQuantity}）"));
        }

        GoodQuantity += goodQuantity;
        DefectQuantity += defectQuantity;
        ScrapQuantity += scrapQuantity;
        StartedAt ??= DateTime.UtcNow;

        Status = ReportedQuantity >= PlannedQuantity
            ? WorkOrderOperationStatus.Completed
            : WorkOrderOperationStatus.InProgress;

        if (Status == WorkOrderOperationStatus.Completed)
        {
            CompletedAt = DateTime.UtcNow;
        }

        return Result.Success();
    }

    /// <summary>是否已完成。</summary>
    public bool IsCompleted => Status == WorkOrderOperationStatus.Completed;
}
