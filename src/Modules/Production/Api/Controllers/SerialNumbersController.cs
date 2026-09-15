using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Production.Api.Controllers;

/// <summary>
/// SN（序列号）与过站：生成、进站 / 出站、追溯查询。
/// </summary>
[ApiController]
[Route("api/production/serial-numbers")]
[Authorize]
public class SerialNumbersController(ISerialNumberService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] Guid? workOrderId = null,
        [FromQuery] int? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new SerialNumberQueryRequest(
                workOrderId,
                status is null ? null : (QiaoMES.Production.Domain.SerialNumberStatus)status,
                keyword,
                page,
                pageSize),
            cancellationToken));

    /// <summary>批量生成 SN（编号规则：工单号-0001）。</summary>
    [HttpPost("generate")]
    [HasPermission(Permissions.WorkOrders.Report)]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateSerialNumbersRequest request,
        CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GenerateAsync(request, cancellationToken));

    /// <summary>按 SN 查询当前状态与完整过站轨迹（追溯）。</summary>
    [HttpGet("{sn}")]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetBySn(string sn, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetBySnAsync(sn, cancellationToken));

    [HttpPost("{sn}/track-in")]
    [HasPermission(Permissions.WorkOrders.Report)]
    public async Task<IActionResult> TrackIn(string sn, [FromBody] SnTrackInRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.TrackInAsync(sn, request, cancellationToken));

    [HttpPost("{sn}/track-out")]
    [HasPermission(Permissions.WorkOrders.Report)]
    public async Task<IActionResult> TrackOut(string sn, [FromBody] SnTrackOutRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.TrackOutAsync(sn, request, cancellationToken));

    [HttpPost("{sn}/scrap")]
    [HasPermission(Permissions.WorkOrders.Report)]
    public async Task<IActionResult> Scrap(string sn, [FromQuery] string? remark, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ScrapAsync(sn, remark, cancellationToken));
}
