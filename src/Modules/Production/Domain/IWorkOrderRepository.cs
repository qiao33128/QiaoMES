namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单仓储接口。
/// </summary>
public interface IWorkOrderRepository
{
    Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkOrder?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkOrder>> GetByStatusAsync(WorkOrderStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkOrder>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> IsOrderNumberTakenAsync(string orderNumber, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    void Add(WorkOrder workOrder);
    void Update(WorkOrder workOrder);
    void AddReport(ProductionReport report);

    /// <summary>提交当前上下文的所有变更。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
