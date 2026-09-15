using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.MasterData.Application;
using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.MasterData.Api.Controllers;

/// <summary>工艺路线。</summary>
[ApiController]
[Route("api/master-data/routings")]
[Authorize]
public class RoutingsController(IRoutingService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.MasterData.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? productId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new RoutingQueryRequest(productId, isActive, keyword, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.MasterData.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateRoutingRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoutingRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpdateAsync(id, request, cancellationToken));

    /// <summary>设为生效版本（同产品其它版本自动失效）。</summary>
    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ActivateAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.DeleteAsync(id, cancellationToken));
}
