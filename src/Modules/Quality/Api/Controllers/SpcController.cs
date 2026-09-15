using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Quality.Api.Controllers;

/// <summary>SPC：检验项趋势与判异。</summary>
[ApiController]
[Route("api/quality/spc")]
[Authorize]
public class SpcController(ISpcService service) : ControllerBase
{
    /// <summary>取某检验项的趋势数据与判异结论。</summary>
    [HttpGet("trend")]
    [HasPermission(Permissions.Quality.Read)]
    public async Task<IActionResult> GetTrend(
        [FromQuery] string itemName,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int points = 30,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetTrendAsync(
            new SpcTrendRequest(itemName, from, to, points), cancellationToken));
}
