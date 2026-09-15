using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Reporting.Api.Controllers;

/// <summary>
/// 班次定义与生产日历：全系统报表的统一时间口径。
/// </summary>
[ApiController]
[Route("api/reporting")]
[Authorize]
public class ReportingShiftsController(IShiftService service) : ControllerBase
{
    // ---------------- 班次 ----------------

    [HttpGet("shifts")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetShifts(
        [FromQuery] bool? isActive = null,
        [FromQuery] string? lineName = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new ShiftQueryRequest(isActive, lineName, page, pageSize), cancellationToken));

    [HttpGet("shifts/{id:guid}")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetShift(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost("shifts")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> CreateShift([FromBody] CreateShiftRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPut("shifts/{id:guid}")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> UpdateShift(Guid id, [FromBody] UpdateShiftRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpdateAsync(id, request, cancellationToken));

    [HttpPut("shifts/{id:guid}/active")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> SetShiftActive(Guid id, [FromQuery] bool isActive, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SetActiveAsync(id, isActive, cancellationToken));

    /// <summary>当前所处班次（含生产日），大屏与现场看板用。</summary>
    [HttpGet("shifts/current")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetCurrentShift(
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetCurrentAsync(lineName, null, cancellationToken));

    /// <summary>生产日区间 → 班次时间窗集合（UTC 边界），报表聚合口径。</summary>
    [HttpGet("shifts/ranges")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetShiftRanges(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetRangesAsync(new ShiftRangeRequest(from, to, lineName), cancellationToken));

    // ---------------- 日历 ----------------

    [HttpGet("calendar")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetCalendar(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetCalendarAsync(
            new CalendarQueryRequest(from, to, page, pageSize), cancellationToken));

    /// <summary>设置某天为节假日或调休工作日。</summary>
    [HttpPost("calendar")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> UpsertCalendarDay([FromBody] UpsertCalendarDayRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpsertCalendarDayAsync(request, cancellationToken));
}
