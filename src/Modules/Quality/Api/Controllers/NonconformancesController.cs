using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Quality.Api.Controllers;

/// <summary>
/// 不合格品处置（NCR）：处置决策 → 返工/返修 → 复检 → 关闭。
/// </summary>
[ApiController]
[Route("api/quality/nonconformances")]
[Authorize]
public class NonconformancesController(INonconformanceService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? status = null,
        [FromQuery] int? disposition = null,
        [FromQuery] string? keyword = null,
        [FromQuery] Guid? workOrderId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new NonconformanceQueryRequest(
                status is null ? null : (QiaoMES.Quality.Domain.DispositionStatus)status,
                disposition is null ? null : (QiaoMES.Quality.Domain.DispositionType)disposition,
                keyword,
                workOrderId,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateNonconformanceRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    /// <summary>决定处置方式。</summary>
    [HttpPost("{id:guid}/decide")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Decide(Guid id, [FromBody] DecideNonconformanceRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.DecideAsync(id, request, cancellationToken));

    /// <summary>登记一次维修 / 返工。</summary>
    [HttpPost("{id:guid}/repairs")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> StartRepair(Guid id, [FromBody] StartRepairRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.StartRepairAsync(id, request, cancellationToken));

    /// <summary>完成维修。</summary>
    [HttpPost("{id:guid}/repairs/complete")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> CompleteRepair(Guid id, [FromBody] CompleteRepairRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CompleteRepairAsync(id, request, cancellationToken));

    /// <summary>复检结果（合格关闭 / 不合格退回处理中）。</summary>
    [HttpPost("{id:guid}/reinspect")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Reinspect(Guid id, [FromBody] ReinspectResultRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ReinspectAsync(id, request, cancellationToken));

    /// <summary>报废关闭。</summary>
    [HttpPost("{id:guid}/scrap")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Scrap(Guid id, [FromQuery] string? remark, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ScrapAsync(id, remark, cancellationToken));
}
