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
/// 设备台账、状态管理、点检保养与停机分析。
/// </summary>
[ApiController]
[Route("api/equipment/equipments")]
[Authorize]
public class EquipmentsController(IEquipmentService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Equipment.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? keyword = null,
        [FromQuery] int? status = null,
        [FromQuery] string? lineName = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(
            new EquipmentQueryRequest(
                keyword,
                status is null ? null : (EquipmentStatus)status,
                lineName,
                isActive,
                page,
                pageSize),
            cancellationToken));

    /// <summary>设备状态汇总（看板红黄绿）。</summary>
    [HttpGet("summary")]
    [HasPermission(Permissions.Equipment.Read)]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetStatusSummaryAsync(cancellationToken));

    /// <summary>停机原因 Pareto。</summary>
    [HttpGet("downtime-pareto")]
    [HasPermission(Permissions.Equipment.Read)]
    public async Task<IActionResult> GetDowntimePareto(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetDowntimeParetoAsync(from, to, top, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Equipment.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Equipment.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateEquipmentRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Equipment.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEquipmentRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.UpdateAsync(id, request, cancellationToken));

    /// <summary>切换设备状态（运行 / 待机 / 故障 / 保养 / 离线）。故障必须带停机原因。</summary>
    [HttpPost("{id:guid}/status")]
    [HasPermission(Permissions.Equipment.Operate)]
    public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeEquipmentStatusRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ChangeStatusAsync(id, request, cancellationToken));

    [HttpPut("{id:guid}/active")]
    [HasPermission(Permissions.Equipment.Manage)]
    public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool isActive, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SetActiveAsync(id, isActive, cancellationToken));

    /// <summary>登记点检 / 保养 / 维修记录。</summary>
    [HttpPost("{id:guid}/maintenance")]
    [HasPermission(Permissions.Equipment.Operate)]
    public async Task<IActionResult> AddMaintenance(Guid id, [FromBody] CreateMaintenanceRecordRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.AddMaintenanceAsync(id, request, cancellationToken));
}
