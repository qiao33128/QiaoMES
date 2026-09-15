using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Quality.Api.Controllers;

/// <summary>不良代码主数据。</summary>
[ApiController]
[Route("api/quality/defect-codes")]
[Authorize]
public class DefectCodesController(IDefectCodeService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? keyword = null,
        [FromQuery] string? category = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new DefectCodeQueryRequest(keyword, category, isActive, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    /// <summary>不良 Pareto（默认最近 30 天 TOP 10）。</summary>
    [HttpGet("pareto")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetPareto(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetParetoAsync(from, to, top, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateDefectCodeRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateDefectCodeRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpdateAsync(id, request, cancellationToken));

    [HttpPut("{id:guid}/status")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> SetStatus(Guid id, [FromQuery] bool isActive, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SetActiveAsync(id, isActive, cancellationToken));
}
