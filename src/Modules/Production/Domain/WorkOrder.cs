using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

/// <summary>
/// 生产工单。
/// </summary>
public class WorkOrder : Entity
{
    private WorkOrder() { }

    public WorkOrder(
        string orderNumber,
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
    public string ProductCode { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public int PlannedQuantity { get; private set; }
    public int CompletedQuantity { get; private set; }
    public WorkOrderStatus Status { get; private set; }
    public DateTime? PlannedStart { get; private set; }
    public DateTime? PlannedEnd { get; private set; }
    public string? WorkCenter { get; private set; }
    public string? Remark { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    private readonly List<ProductionReport> _reports = [];
    public IReadOnlyCollection<ProductionReport> Reports => _reports.AsReadOnly();

    /// <summary>下达工单（草稿 → 已下达）。</summary>
    public Result Release()
    {
        if (Status != WorkOrderStatus.Draft)
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "只有草稿状态的工单才能下达"));
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

    /// <summary>报工（记录完成数量）。</summary>
    public Result Report(int quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("WorkOrder.InvalidQuantity", "报工数量必须大于 0"));
        }
        if (Status != WorkOrderStatus.InProgress)
        {
            return Result.Failure(Error.Conflict("WorkOrder.InvalidTransition", "只有生产中的工单才能报工"));
        }
        if (CompletedQuantity + quantity > PlannedQuantity)
        {
            return Result.Failure(Error.Validation("WorkOrder.OverProduction", "报工数量超出计划数量"));
        }

        CompletedQuantity += quantity;

        if (CompletedQuantity >= PlannedQuantity)
        {
            Complete();
        }
        return Result.Success();
    }

    /// <summary>
    /// 创建一条生产报工记录（由调用方通过仓储显式持久化，避免 EF 导航集合追踪混淆）。
    /// </summary>
    public ProductionReport CreateReport(int quantity)
    {
        return new ProductionReport(Id, quantity, DateTime.UtcNow);
    }

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

    /// <summary>更新计划信息。</summary>
    public void UpdatePlan(string productName, int plannedQuantity, DateTime? plannedStart, DateTime? plannedEnd,
        string? workCenter, string? remark)
    {
        ProductName = productName;
        PlannedQuantity = plannedQuantity;
        PlannedStart = plannedStart;
        PlannedEnd = plannedEnd;
        WorkCenter = workCenter;
        Remark = remark;
    }
}
