using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Infrastructure.Integrations;
using QiaoMES.MasterData.Application;
using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
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
    IProductService productService,
    IMaterialService materialService,
    IBomService bomService,
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

    // ---------------- 主数据交换（产品 / 物料 / BOM）----------------

    /// <summary>
    /// 产品编码 → 产品 Id 映射。<para>
    /// ERP 下发工单前先用本接口把内部编码换成 ProductId（工单接口要求 ProductId）。
    /// 停用的产品仍可能被引用，故默认返回全部；只要「启用中」的请传 <c>isActive=true</c>。
    /// </para>
    /// </summary>
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? keyword = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await productService.GetListAsync(
            new CatalogQueryRequest(page, pageSize, keyword, isActive), cancellationToken);

        return result.IsFailure
            ? ApiResults.Problem(result.Error)
            : Ok(new { totalCount = result.Value.TotalCount, items = result.Value.Items });
    }

    /// <summary>物料查询（编码映射 / 对账）。传 <c>isActive=true</c> 只取启用中的物料。</summary>
    [HttpGet("materials")]
    public async Task<IActionResult> GetMaterials(
        [FromQuery] string? keyword = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await materialService.GetListAsync(
            new CatalogQueryRequest(page, pageSize, keyword, isActive), cancellationToken);

        return result.IsFailure
            ? ApiResults.Problem(result.Error)
            : Ok(new { totalCount = result.Value.TotalCount, items = result.Value.Items });
    }

    /// <summary>
    /// 物料下发。<para>**幂等键 = 物料编码**：编码已存在直接返回既有物料，不会重复建档。</para>
    /// </summary>
    [HttpPost("materials")]
    public async Task<IActionResult> ImportMaterial(
        [FromBody] ImportMaterialRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResults.Problem(Error.Validation("OpenApi.InvalidMaterial", "物料编码与名称不能为空"));
        }

        var code = request.Code.Trim();
        var existing = await materialService.GetListAsync(new CatalogQueryRequest(1, 5, code), cancellationToken);
        if (existing.IsFailure)
        {
            return ApiResults.Problem(existing.Error);
        }

        var matched = existing.Value.Items.FirstOrDefault(item =>
            string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));

        if (matched is not null)
        {
            return Ok(new { idempotent = true, material = matched });
        }

        var created = await materialService.CreateAsync(
            new CreateMaterialRequest(
                code,
                request.Name,
                (MaterialType)Math.Clamp(request.MaterialType, 0, 4),
                request.SupplierPartNumber,
                request.Spec,
                request.Unit,
                request.Remark),
            cancellationToken);

        if (created.IsFailure)
        {
            return ApiResults.Problem(created.Error);
        }

        return StatusCode(StatusCodes.Status201Created, new { idempotent = false, material = created.Value });
    }

    /// <summary>BOM 查询（按产品 / 版本关键字）。</summary>
    [HttpGet("boms")]
    public async Task<IActionResult> GetBoms(
        [FromQuery] Guid? productId = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await bomService.GetListAsync(
            new BomQueryRequest(productId, null, keyword, page, pageSize), cancellationToken);

        return result.IsFailure
            ? ApiResults.Problem(result.Error)
            : Ok(new { totalCount = result.Value.TotalCount, items = result.Value.Items });
    }

    /// <summary>
    /// BOM 下发。<para>**幂等键 = 产品 + 版本**：同产品同版本已存在则返回既有 BOM，不会重复建版本。</para>
    /// </summary>
    [HttpPost("boms")]
    public async Task<IActionResult> ImportBom(
        [FromBody] ImportBomRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(request.Version))
        {
            return ApiResults.Problem(Error.Validation("OpenApi.InvalidBom", "ProductId 与版本号不能为空"));
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return ApiResults.Problem(Error.Validation("OpenApi.EmptyBomItems", "BOM 明细不能为空"));
        }

        var version = request.Version.Trim();
        var existing = await bomService.GetListAsync(
            new BomQueryRequest(request.ProductId, null, version, 1, 10), cancellationToken);

        if (existing.IsFailure)
        {
            return ApiResults.Problem(existing.Error);
        }

        var matched = existing.Value.Items.FirstOrDefault(bom =>
            string.Equals(bom.Version, version, StringComparison.OrdinalIgnoreCase));

        if (matched is not null)
        {
            return Ok(new { idempotent = true, bom = matched });
        }

        var created = await bomService.CreateAsync(
            new CreateBomRequest(
                request.ProductId,
                version,
                request.Remark,
                request.Items
                    .Select(item => new BomItemRequest(item.MaterialId, item.Quantity, item.Unit, item.LossRate))
                    .ToList()),
            cancellationToken);

        if (created.IsFailure)
        {
            return ApiResults.Problem(created.Error);
        }

        return StatusCode(StatusCodes.Status201Created, new { idempotent = false, bom = created.Value });
    }
}

/// <summary>物料下发（幂等键 = 物料编码）。</summary>
/// <param name="MaterialType">0 原材料 / 1 半成品 / 2 成品 / 3 包装 / 4 辅料。</param>
public record ImportMaterialRequest(
    string Code,
    string Name,
    int MaterialType = 0,
    string? SupplierPartNumber = null,
    string? Spec = null,
    string? Unit = null,
    string? Remark = null);

public record ImportBomItemRequest(Guid MaterialId, decimal Quantity, string? Unit = null, decimal LossRate = 0);

/// <summary>BOM 下发（幂等键 = 产品 + 版本）。</summary>
public record ImportBomRequest(
    Guid ProductId,
    string Version,
    string? Remark = null,
    IReadOnlyList<ImportBomItemRequest>? Items = null);
