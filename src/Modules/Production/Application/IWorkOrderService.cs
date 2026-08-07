using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application;

/// <summary>
/// 工单应用服务。
/// </summary>
public interface IWorkOrderService
{
    Task<Result<WorkOrderDto>> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<WorkOrderDto>>> GetListAsync(PaginationRequest pagination, WorkOrderStatus? status = null, string? keyword = null, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> ReleaseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> StartProductionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> ReportAsync(Guid id, ReportRequest request, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<WorkOrderDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default);
}
