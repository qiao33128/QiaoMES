using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Quality.Api.Controllers;

/// <summary>
/// 检验单：IQC / IPQC / FQC / OQC 的创建、录入与判定。
/// </summary>
[ApiController]
[Route("api/quality/inspections")]
[Authorize]
public class InspectionsController(IInspectionService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? type = null,
        [FromQuery] int? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] Guid? workOrderId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new InspectionQueryRequest(
                type is null ? null : (QiaoMES.Quality.Domain.InspectionType)type,
                status is null ? null : (QiaoMES.Quality.Domain.InspectionStatus)status,
                keyword,
                workOrderId,
                from,
                to,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    /// <summary>按 SN 查询检验历史（追溯）。</summary>
    [HttpGet("by-sn/{sn}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetBySn(string sn, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetBySnAsync(sn, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Quality.Inspect)]
    public async Task<IActionResult> Create([FromBody] CreateInspectionRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPost("{id:guid}/items")]
    [HasPermission(Permissions.Quality.Inspect)]
    public async Task<IActionResult> AddItems(Guid id, [FromBody] AddInspectionItemsRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.AddItemsAsync(id, request, cancellationToken));

    /// <summary>录入某个检验项的结果（定量项按规格上下限自动判定）。</summary>
    [HttpPut("{id:guid}/items/record")]
    [HasPermission(Permissions.Quality.Inspect)]
    public async Task<IActionResult> RecordItem(Guid id, [FromBody] RecordInspectionItemRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.RecordItemAsync(id, request, cancellationToken));

    /// <summary>提交判定；不合格且勾选 createNonconformance 时自动派生处置单。</summary>
    [HttpPost("{id:guid}/submit")]
    [HasPermission(Permissions.Quality.Inspect)]
    public async Task<IActionResult> Submit(Guid id, [FromBody] SubmitInspectionRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SubmitAsync(id, request, cancellationToken));
}
