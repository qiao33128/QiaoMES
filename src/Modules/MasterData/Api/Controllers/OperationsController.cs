using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.MasterData.Application;
using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.MasterData.Api.Controllers;

/// <summary>工序主数据。</summary>
[ApiController]
[Route("api/master-data/operations")]
[Authorize]
public class OperationsController(IOperationService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.MasterData.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new CatalogQueryRequest(page, pageSize, keyword, isActive), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterData.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateOperationRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOperationRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpdateAsync(id, request, cancellationToken));

    [HttpPut("{id:guid}/status")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SetActiveAsync(id, request, cancellationToken));
}
