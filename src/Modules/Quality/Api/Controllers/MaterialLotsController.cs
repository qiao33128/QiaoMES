using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Quality.Api.Controllers;

/// <summary>
/// 来料批次与批次谱系：批次维护、IQC 准入、SN 绑定批次、正向 / 反向追溯。
/// </summary>
[ApiController]
[Route("api/quality")]
[Authorize]
public class MaterialLotsController(IMaterialLotService service) : ControllerBase
{
    // ---------------- 批次 ----------------

    [HttpGet("material-lots")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? keyword = null,
        [FromQuery] string? materialCode = null,
        [FromQuery] int? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new MaterialLotQueryRequest(
                keyword,
                materialCode,
                status is null ? null : (MaterialLotStatus)status,
                from,
                to,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("material-lots/{id:guid}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    /// <summary>按批次号取详情（含已流向 SN 数与消耗量）。</summary>
    [HttpGet("material-lots/by-lot-number/{lotNumber}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetByLotNumber(string lotNumber, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByLotNumberAsync(lotNumber, cancellationToken));

    [HttpPost("material-lots")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateMaterialLotRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    /// <summary>登记 IQC 结论（决定批次能否投产）。</summary>
    [HttpPost("material-lots/{id:guid}/inspect")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> Inspect(Guid id, [FromBody] InspectMaterialLotRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.InspectAsync(id, request, cancellationToken));

    /// <summary>冻结 / 解冻批次。</summary>
    [HttpPost("material-lots/{id:guid}/freeze")]
    [HasPermission(Permissions.Quality.Manage)]
    public async Task<IActionResult> SetFrozen(Guid id, [FromBody] FreezeMaterialLotRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SetFrozenAsync(id, request, cancellationToken));

    // ---------------- 谱系绑定与追溯 ----------------

    /// <summary>SN 绑定来料批次（批量、幂等，绑定即扣减批次余量）。</summary>
    [HttpPost("material-consumptions")]
    [HasPermission(Permissions.Quality.Inspect)]
    public async Task<IActionResult> Bind([FromBody] BindMaterialConsumptionsRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.BindConsumptionsAsync(request, cancellationToken));

    /// <summary>正向追溯：某 SN 消耗了哪些来料批次。</summary>
    [HttpGet("material-consumptions/by-sn/{sn}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetBySn(string sn, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetConsumptionsBySnAsync(sn, cancellationToken));

    /// <summary>反向追溯：某批次流向了哪些 SN（客诉定位同批影响范围）。</summary>
    [HttpGet("material-consumptions/by-lot/{lotNumber}")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetByLot(
        string lotNumber,
        [FromQuery] int take = 200,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetLotTraceAsync(lotNumber, take, cancellationToken));
}
