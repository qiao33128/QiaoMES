using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Api.Controllers;

[ApiController]
[Route("api/work-orders")]
[Authorize]
public class WorkOrderController(IWorkOrderService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkOrderDto>>> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] WorkOrderStatus? status = null,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetListAsync(new PaginationRequest(page, pageSize), status, keyword, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkOrderDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : NotFound(result.Error);
    }

    [HttpPost]
    public async Task<ActionResult<WorkOrderDto>> Create([FromBody] CreateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WorkOrderDto>> Update(Guid id, [FromBody] UpdateWorkOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/release")]
    public async Task<ActionResult<WorkOrderDto>> Release(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.ReleaseAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/start")]
    public async Task<ActionResult<WorkOrderDto>> Start(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.StartProductionAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/report")]
    public async Task<ActionResult<WorkOrderDto>> Report(Guid id, [FromBody] ReportRequest request, CancellationToken cancellationToken)
    {
        var result = await service.ReportAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<WorkOrderDto>> Complete(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.CompleteAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<WorkOrderDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.CancelAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : BadRequest(result.Error);
    }
}
