using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Production.Api.Controllers;

/// <summary>
/// 工单接口。每个动作都绑定到具体权限，而不只是「已登录」。
/// </summary>
[ApiController]
[Route("api/work-orders")]
[Authorize]
public class WorkOrderController(IWorkOrderService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = WorkOrderQuery.DefaultPageSize,
        [FromQuery] WorkOrderStatus? status = null,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetListAsync(new PaginationRequest(page, pageSize), status, keyword, cancellationToken);
        return ApiResults.FromResult(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.WorkOrders.Create)]
    public async Task<IActionResult> Create([FromBody] CreateWorkOrderRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.WorkOrders.Update)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkOrderRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/release")]
    [HasPermission(Permissions.WorkOrders.Release)]
    public async Task<IActionResult> Release(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ReleaseAsync(id, cancellationToken));

    [HttpPost("{id:guid}/start")]
    [HasPermission(Permissions.WorkOrders.Start)]
    public async Task<IActionResult> Start(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.StartProductionAsync(id, cancellationToken));

    /// <summary>工序级报工（良品 / 不良 / 报废）。前序工序未完成时会被拒绝。</summary>
    [HttpPost("{id:guid}/operations/{operationTaskId:guid}/report")]
    [HasPermission(Permissions.WorkOrders.Report)]
    public async Task<IActionResult> ReportOperation(
        Guid id,
        Guid operationTaskId,
        [FromBody] ReportOperationRequest request,
        CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ReportOperationAsync(id, operationTaskId, request, cancellationToken));

    [HttpPost("{id:guid}/complete")]
    [HasPermission(Permissions.WorkOrders.Complete)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CompleteAsync(id, cancellationToken));

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.WorkOrders.Cancel)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CancelAsync(id, cancellationToken));
}
