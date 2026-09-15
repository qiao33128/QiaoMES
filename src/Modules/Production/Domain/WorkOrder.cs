using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

/// <summary>
/// 生产工单。
/// <para>工单下达时按产品的生效工艺路线展开工序任务，并快照所用 BOM / 工艺路线版本。</para>
/// </summary>
public class WorkOrder : Entity
{
    private WorkOrder() { }

    public WorkOrder(
        string orderNumber,
        Guid productId,
        string productCode,
        string productName,
        int plannedQuantity,
        DateTime? plannedStart,
        DateTime? plannedEnd,
        string? workCenter = null,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        OrderNumber = orderNumber;
        ProductId = productId;
        ProductCode = productCode;
        ProductName = productName;
        PlannedQuantity = plannedQuantity;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        WorkCenter = workCenter;
        Remark = remark;
        Status = WorkOrderStatus.Draft;
        CreatedAt = DateTime.UtcNow;
    }

    public string OrderNumber { get; private set; } = string.Empty;

    public Guid ProductId { get; private set; }

    /// <summary>产品编码（创建时快照）。</summary>
    public string ProductCode { get; private set; } = string.Empty;

    /// <summary>产品名称（创建时快照）。</summary>
    public string ProductName { get; private set; } = string.Empty;

    public int PlannedQuantity { get; private set; }

    /// <summary>工单完成数：取最后一道工序的良品数。</summary>
    public int CompletedQuantity { get; private set; }

    public WorkOrderStatus Status { get; private set; }

    public DateTime? PlannedStart { get; private set; }

    public DateTime? PlannedEnd { get; private set; }

    public string? WorkCenter { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    // ---------- 下达时快照的主数据版本 ----------

    public Guid? RoutingId { get; private set; }

    public string? RoutingVersion { get; private set; }

    public Guid? BomId { get; private set; }

    public string? BomVersion { get; private set; }

    private readonly List<WorkOrderOperation> _operations = [];
    public IReadOnlyCollection<WorkOrderOperation> Operations => _operations.AsReadOnly();

    private readonly List<ProductionReport> _reports = [];
    public IReadOnlyCollection<ProductionReport> Reports => _reports.AsReadOnly();

    /// <summary>按顺序排列的工序任务。</summary>
    public IReadOnlyList<WorkOrderOperation> OrderedOperations => _operations.OrderBy(o => o.Sequence).ToList();

    /// <summary>
    /// 下达工单（草稿 → 已下达）：按工艺路线快照展开工序任务。
    /// </summary>
    /// <returns>新展开的工序任务通过 <see cref="Operations"/> 读取，调用方需显式持久化。</returns>
    public Result Release(RoutingReleaseSnapshot? routing)
    {
        if (Status != WorkOrderStatus.Draft)
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "只有草稿状态的工单才能下达"));
        }
        if (routing is null || routing.Steps.Count == 0)
        {
            return Result.Failure(Error.Conflict("WorkOrder.NoRouting", "产品没有生效的工艺路线，无法下达"));
        }

        RoutingId = routing.RoutingId;
        RoutingVersion = routing.Version;
        BomId = routing.BomId;
        BomVersion = routing.BomVersion;

        _operations.Clear();
        foreach (var step in routing.Steps.OrderBy(s => s.Sequence))
        {
            _operations.Add(new WorkOrderOperation(Id, step, PlannedQuantity));
        }

        Status = WorkOrderStatus.Released;
        return Result.Success();
    }

    /// <summary>开始生产（已下达 → 生产中）。</summary>
    public Result StartProduction()
    {
        if (Status != WorkOrderStatus.Released)
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "只有已下达状态的工单才能开始生产"));
        }

        Status = WorkOrderStatus.InProgress;
        return Result.Success();
    }

    /// <summary>
    /// 工序级报工。要求前序工序已完成，不允许跳序报工。
    /// </summary>
    public Result ReportOperation(Guid operationId, int goodQuantity, int defectQuantity, int scrapQuantity)
    {
        if (Status != WorkOrderStatus.InProgress)
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "只有生产中的工单才能报工"));
        }

        var operation = _operations.FirstOrDefault(o => o.Id == operationId);
        if (operation is null)
        {
            return Result.Failure(Error.NotFound("WorkOrder.OperationNotFound", "工序任务不存在"));
        }

        var previous = _operations
            .Where(o => o.Sequence < operation.Sequence)
            .OrderByDescending(o => o.Sequence)
            .FirstOrDefault();

        if (previous is not null && !previous.IsCompleted)
        {
            return Result.Failure(Error.Conflict(
                "WorkOrder.PreviousOperationNotCompleted",
                $"前序工序「{previous.OperationName}」尚未完成，不能跳序报工"));
        }

        var result = operation.Report(goodQuantity, defectQuantity, scrapQuantity);
        if (result.IsFailure)
        {
            return result;
        }

        // 工单产量 = 最后一道工序的良品数
        CompletedQuantity = _operations.OrderByDescending(o => o.Sequence).First().GoodQuantity;

        if (_operations.Count > 0 && _operations.All(o => o.IsCompleted))
        {
            Complete();
        }

        return Result.Success();
    }

    /// <summary>创建一条工序报工记录（由调用方通过仓储显式持久化）。</summary>
    public ProductionReport CreateReport(
        Guid workOrderOperationId,
        int goodQuantity,
        int defectQuantity,
        int scrapQuantity,
        string? defectCode = null,
        Guid? operatorId = null,
        string? remark = null)
        => new(Id, workOrderOperationId, goodQuantity, defectQuantity, scrapQuantity, defectCode, operatorId, remark);

    /// <summary>完成工单（生产完成）。</summary>
    public Result Complete()
    {
        if (Status is not (WorkOrderStatus.InProgress or WorkOrderStatus.Released))
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "当前状态无法完成工单"));
        }

        Status = WorkOrderStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>取消工单。</summary>
    public Result Cancel()
    {
        if (Status is WorkOrderStatus.Completed or WorkOrderStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "已结束的工单无法取消"));
        }

        Status = WorkOrderStatus.Cancelled;
        return Result.Success();
    }

    /// <summary>更新计划信息（仅草稿状态可用）。</summary>
    public void UpdatePlan(
        Guid productId,
        string productCode,
        string productName,
        int plannedQuantity,
        DateTime? plannedStart,
        DateTime? plannedEnd,
        string? workCenter,
        string? remark)
    {
        ProductId = productId;
        ProductCode = productCode;
        ProductName = productName;
        PlannedQuantity = plannedQuantity;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        WorkCenter = workCenter;
        Remark = remark;
    }
}
