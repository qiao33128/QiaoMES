namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单仓储接口。
/// </summary>
public interface IWorkOrderRepository
{
    Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkOrder?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按条件分页查询（筛选、排序、分页全部下推到数据库）。
    /// </summary>
    /// <returns>当前页数据与符合条件的总记录数。</returns>
    Task<(IReadOnlyList<WorkOrder> Items, int TotalCount)> QueryAsync(
        WorkOrderQuery query,
        CancellationToken cancellationToken = default);

    void Add(WorkOrder workOrder);

    /// <summary>
    /// 显式持久化工序任务（工单下达时按工艺路线展开）。
    /// <para>EF 会把「通过导航集合发现、主键已有值」的子实体判为 Modified，必须显式 Add。</para>
    /// </summary>
    void AddOperation(WorkOrderOperation operation);

    /// <summary>
    /// 仅用于「未受变更跟踪」的工单（分离场景）。
    /// <para>
    /// 仓储查询返回的工单本身已被跟踪，此时调用本方法会把导航图中的新实体误标为 Modified，
    /// 请直接修改实体属性后调用 <see cref="SaveChangesAsync"/>。
    /// </para>
    /// </summary>
    void Update(WorkOrder workOrder);

    /// <summary>显式持久化报工记录（新实体不会被 EF 通过导航集合自动判定为新增）。</summary>
    void AddReport(ProductionReport report);

    /// <summary>提交当前上下文的所有变更（事务由上层工作单元统一管理）。</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
