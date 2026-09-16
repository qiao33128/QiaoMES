using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Infrastructure.Integrations;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Shared;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Api.Controllers;

/// <summary>ERP 下发工单请求。</summary>
/// <param name="OrderNumber">外部工单号，同时作为**幂等键**：重复推送不会重复建单。</param>
/// <param name="ProductId">产品 Id（来自主数据，可由 <c>GET /api/open/v1/products</c> 获取）。</param>
public record ImportWorkOrderRequest(
    string OrderNumber,
    Guid ProductId,
    int PlannedQuantity,
    DateTime? PlannedStart = null,
    DateTime? PlannedEnd = null,
    string? WorkCenter = null,
    string? Remark = null);

/// <summary>设备数据采集上报。</summary>
/// <param name="Status">设备状态（0 运行 / 1 待机 / 2 故障 / 3 保养 / 4 离线）。</param>
public record EquipmentTelemetryRequest(
    string EquipmentCode,
    int Status,
    string? ReasonCode = null,
    string? Reason = null,
    DateTime? ReportedAt = null);

/// <summary>
/// 开放 API v1：给 ERP / 设备采集网关等外部系统使用。<para>
/// 鉴权走独立的 <c>X-Api-Key</c> 方案（与内部 JWT 分离），限流按密钥分片（120 次 / 分钟）。
/// </para>
/// </summary>
[ApiController]
[Route("api/open/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthenticationDefaults.Scheme)]
[EnableRateLimiting(IntegrationExtensions.OpenApiRateLimitPolicy)]
public class OpenApiController(
    IWorkOrderService workOrderService,
    IOutboxWriter outboxWriter) : ControllerBase
{
    /// <summary>
    /// ERP 下发工单。<para>幂等：按工单号查找，已存在则返回既有工单（<c>idempotent=true</c>），不会重复建单。</para>
    /// </summary>
    [HttpPost("work-orders")]
    public async Task<IActionResult> ImportWorkOrder(
        [FromBody] ImportWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            return ApiResults.Problem(Error.Validation("OpenApi.MissingOrderNumber", "工单号不能为空"));
        }
        if (request.PlannedQuantity <= 0)
        {
            return ApiResults.Problem(Error.Validation("OpenApi.InvalidQuantity", "计划数量必须大于 0"));
        }

        var orderNumber = request.OrderNumber.Trim();

        var existing = await workOrderService.GetListAsync(
            new PaginationRequest(1, 10), null, orderNumber, cancellationToken);

        if (existing.IsFailure)
        {
            return ApiResults.Problem(existing.Error);
        }

        var matched = existing.Value.Items.FirstOrDefault(order =>
            string.Equals(order.OrderNumber, orderNumber, StringComparison.OrdinalIgnoreCase));

        if (matched is not null)
        {
            return Ok(new { idempotent = true, workOrder = matched });
        }

        var created = await workOrderService.CreateAsync(
            new CreateWorkOrderRequest(
                request.ProductId,
                request.PlannedQuantity,
                request.PlannedStart,
                request.PlannedEnd,
                request.WorkCenter,
                request.Remark),
            cancellationToken);

        if (created.IsFailure)
        {
            return ApiResults.Problem(created.Error);
        }

        return StatusCode(StatusCodes.Status201Created, new { idempotent = false, workOrder = created.Value });
    }

    /// <summary>ERP 回读工单进度（按工单号）。</summary>
    [HttpGet("work-orders/{orderNumber}")]
    public async Task<IActionResult> GetWorkOrder(string orderNumber, CancellationToken cancellationToken)
    {
        var list = await workOrderService.GetListAsync(
            new PaginationRequest(1, 10), null, orderNumber, cancellationToken);

        if (list.IsFailure)
        {
            return ApiResults.Problem(list.Error);
        }

        var matched = list.Value.Items.FirstOrDefault(order =>
            string.Equals(order.OrderNumber, orderNumber, StringComparison.OrdinalIgnoreCase));

        return matched is null
            ? ApiResults.Problem(Error.NotFound("OpenApi.WorkOrderNotFound", $"工单 {orderNumber} 不存在"))
            : Ok(matched);
    }

    /// <summary>
    /// 设备数据采集上报（MQTT / OPC UA 网关适配后调用本端点，无需在服务器侧引入 broker）。<para>
    /// 接收后立即落 Outbox 并返回 202，状态机变更由设备模块异步应用。
    /// </para>
    /// </summary>
    [HttpPost("equipment-telemetry")]
    public IActionResult ReportTelemetry([FromBody] EquipmentTelemetryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.EquipmentCode))
        {
            return ApiResults.Problem(Error.Validation("OpenApi.MissingEquipmentCode", "设备编码不能为空"));
        }

        outboxWriter.Publish(new EquipmentTelemetryReceivedEvent(
            request.EquipmentCode.Trim(),
            request.Status,
            request.ReasonCode,
            request.Reason,
            request.ReportedAt ?? DateTime.UtcNow));

        return Accepted(new { accepted = true, applied = "async" });
    }
}
