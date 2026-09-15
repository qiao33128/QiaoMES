using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Equipment.Application;
using QiaoMES.Equipment.Application.Contracts;
using QiaoMES.Equipment.Domain;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Equipment.Api.Controllers;

/// <summary>
/// Andon：设备故障 / 质量异常一键呼叫、响应、解决。
/// </summary>
[ApiController]
[Route("api/equipment/andon-calls")]
[Authorize]
public class AndonController(IAndonService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Equipment.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? status = null,
        [FromQuery] int? type = null,
        [FromQuery] int? level = null,
        [FromQuery] bool? onlyOpen = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new AndonQueryRequest(
                status is null ? null : (AndonStatus)status,
                type is null ? null : (AndonType)type,
                level is null ? null : (AndonLevel)level,
                onlyOpen,
                keyword,
                from,
                to,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Equipment.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    /// <summary>一键呼叫（设备故障 / 质量异常 / 缺料）。</summary>
    [HttpPost]
    [HasPermission(Permissions.Equipment.Operate)]
    public async Task<IActionResult> Create([FromBody] CreateAndonCallRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPost("{id:guid}/respond")]
    [HasPermission(Permissions.Equipment.Operate)]
    public async Task<IActionResult> Respond(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.RespondAsync(id, cancellationToken));

    [HttpPost("{id:guid}/resolve")]
    [HasPermission(Permissions.Equipment.Operate)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveAndonRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ResolveAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.Equipment.Operate)]
    public async Task<IActionResult> Close(Guid id, [FromBody] ResolveAndonRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CloseAsync(id, request, cancellationToken));
}
