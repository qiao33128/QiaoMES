using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Equipment.Application;
using QiaoMES.Equipment.Application.Contracts;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>Andon 快照（大屏红黄绿）。</summary>
public record AndonSnapshotDto(
    int Waiting,
    int Responded,
    int Timeout,
    int Escalated,
    int OpenTotal,
    IReadOnlyList<AndonCallDto> RecentCalls);

/// <summary>
/// 车间大屏数据（一次请求拿全，避免大屏轮询多个接口）。
/// </summary>
public record DisplayOverviewDto(
    DateTime GeneratedAt,
    string DateText,
    ProductionStatsDto Production,
    QualityStatsDto Quality,
    EquipmentStatusSummaryDto Equipment,
    AndonSnapshotDto Andon,
    IReadOnlyList<DowntimeParetoDto> DowntimeTop);

/// <summary>
/// 指标聚合（大屏 / 看板）。放在组合根：聚合各模块已有的统计能力，不重复实现业务逻辑。
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(
    ISerialNumberService serialNumberService,
    IInspectionService inspectionService,
    IEquipmentService equipmentService,
    IAndonService andonService) : ControllerBase
{
    /// <summary>大屏总览：产量 / 良率 / 设备状态 / Andon（默认统计本地「今日」）。</summary>
    [HttpGet("overview")]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var localNow = DateTime.Now;
        // 时间戳统一按 UTC 存储，本地「今日零点」需换算成 UTC 再比较
        var rangeFrom = from?.ToUniversalTime() ?? localNow.Date.ToUniversalTime();
        var rangeTo = to?.ToUniversalTime() ?? localNow.ToUniversalTime();

        var productionResult = await serialNumberService.GetStatsAsync(rangeFrom, rangeTo, cancellationToken);
        var qualityResult = await inspectionService.GetStatsAsync(rangeFrom, rangeTo, cancellationToken);
        var equipmentResult = await equipmentService.GetStatusSummaryAsync(cancellationToken);
        var andonResult = await andonService.GetListAsync(
            new AndonQueryRequest(OnlyOpen: true, Page: 1, PageSize: 20), cancellationToken);
        var downtimeResult = await equipmentService.GetDowntimeParetoAsync(
            rangeFrom.AddDays(-30), rangeTo, 5, cancellationToken);

        if (productionResult.IsFailure)
        {
            return ApiResults.Problem(productionResult.Error);
        }
        if (qualityResult.IsFailure)
        {
            return ApiResults.Problem(qualityResult.Error);
        }
        if (equipmentResult.IsFailure)
        {
            return ApiResults.Problem(equipmentResult.Error);
        }
        if (andonResult.IsFailure)
        {
            return ApiResults.Problem(andonResult.Error);
        }

        var calls = andonResult.Value.Items;
        var andon = new AndonSnapshotDto(
            calls.Count(c => c.Status == QiaoMES.Equipment.Domain.AndonStatus.Waiting),
            calls.Count(c => c.Status == QiaoMES.Equipment.Domain.AndonStatus.Responded),
            calls.Count(c => c.IsTimeout),
            calls.Count(c => c.Escalated),
            calls.Count,
            calls);

        return Ok(new DisplayOverviewDto(
            DateTime.UtcNow,
            localNow.ToString("yyyy-MM-dd"),
            productionResult.Value,
            qualityResult.Value,
            equipmentResult.Value,
            andon,
            downtimeResult.IsSuccess ? downtimeResult.Value : []));
    }
}
