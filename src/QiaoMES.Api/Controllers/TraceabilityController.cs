using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.MasterData.Application;
using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Quality.Application;
using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>
/// 追溯报告（人机料法环）。放在组合根：只聚合各模块已有的查询服务，不重复实现业务逻辑。
/// </summary>
/// <param name="Sn">序列号。</param>
/// <param name="SerialNumber">SN 当前状态与流转轨迹（人 / 机 / 时间）。</param>
/// <param name="WorkOrder">所属工单（含工序任务与版本快照）。</param>
/// <param name="Routing">下达时快照的工艺路线（法）。</param>
/// <param name="Bom">下达时快照的 BOM（料）。</param>
/// <param name="Inspections">该 SN 的全部检验记录（环 / 质量）。</param>
/// <param name="Nonconformances">该 SN 的不合格处置与维修记录。</param>
public record TraceabilityReport(
    string Sn,
    SerialNumberDetailDto? SerialNumber,
    WorkOrderDto? WorkOrder,
    RoutingDto? Routing,
    BomDto? Bom,
    IReadOnlyList<InspectionDto> Inspections,
    IReadOnlyList<NonconformanceDto> Nonconformances);

/// <summary>批次影响范围（客诉时快速定位同批全量）。</summary>
/// <param name="WorkOrderId">工单。</param>
/// <param name="OrderNumber">工单号。</param>
/// <param name="TotalCount">SN 总数。</param>
/// <param name="CompletedCount">已完工数。</param>
/// <param name="ScrappedCount">已报废数。</param>
/// <param name="NonconformanceCount">该工单下不合格处置单数量。</param>
/// <param name="InspectionFailCount">不合格检验单数量。</param>
/// <param name="SerialNumbers">SN 明细。</param>
public record BatchTraceabilityReport(
    Guid WorkOrderId,
    string OrderNumber,
    int TotalCount,
    int CompletedCount,
    int ScrappedCount,
    int NonconformanceCount,
    int InspectionFailCount,
    IReadOnlyList<SerialNumberDto> SerialNumbers);

/// <summary>
/// SN 正 / 反向追溯与批次影响范围查询。
/// </summary>
[ApiController]
[Route("api/traceability")]
[Authorize]
public class TraceabilityController(
    ISerialNumberService serialNumberService,
    IWorkOrderService workOrderService,
    IRoutingService routingService,
    IBomService bomService,
    IInspectionService inspectionService,
    INonconformanceService nonconformanceService) : ControllerBase
{
    /// <summary>按 SN 生成完整追溯报告（人机料法环）。</summary>
    [HttpGet("sn/{sn}")]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetBySn(string sn, CancellationToken cancellationToken)
    {
        var serialResult = await serialNumberService.GetBySnAsync(sn, cancellationToken);
        if (serialResult.IsFailure)
        {
            return ApiResults.Problem(serialResult.Error);
        }

        var serialDetail = serialResult.Value;
        var workOrderId = serialDetail.SerialNumber.WorkOrderId;

        // 工单（含工序任务与快照版本）
        WorkOrderDto? workOrder = null;
        var workOrderResult = await workOrderService.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrderResult.IsSuccess)
        {
            workOrder = workOrderResult.Value;
        }

        // 工艺路线与 BOM：按工单下达时快照的版本取
        RoutingDto? routing = null;
        if (workOrder?.RoutingId is not null)
        {
            var routingResult = await routingService.GetByIdAsync(workOrder.RoutingId.Value, cancellationToken);
            routing = routingResult.IsSuccess ? routingResult.Value : null;
        }

        BomDto? bom = null;
        if (workOrder?.BomId is not null)
        {
            var bomResult = await bomService.GetByIdAsync(workOrder.BomId.Value, cancellationToken);
            bom = bomResult.IsSuccess ? bomResult.Value : null;
        }

        // 质量记录
        var inspections = new List<InspectionDto>();
        var inspectionResult = await inspectionService.GetBySnAsync(sn, cancellationToken);
        if (inspectionResult.IsSuccess)
        {
            inspections.AddRange(inspectionResult.Value);
        }

        var nonconformances = new List<NonconformanceDto>();
        var nonconformanceResult = await nonconformanceService.GetListAsync(
            new NonconformanceQueryRequest(Keyword: sn, Page: 1, PageSize: 50), cancellationToken);
        if (nonconformanceResult.IsSuccess)
        {
            nonconformances.AddRange(nonconformanceResult.Value.Items);
        }

        return Ok(new TraceabilityReport(
            sn,
            serialDetail,
            workOrder,
            routing,
            bom,
            inspections,
            nonconformances));
    }

    /// <summary>批次影响范围：某工单下全部 SN 的状态汇总与明细。</summary>
    [HttpGet("batch/{workOrderId:guid}")]
    [HasPermission(Permissions.WorkOrders.Read)]
    public async Task<IActionResult> GetBatch(Guid workOrderId, CancellationToken cancellationToken)
    {
        var serials = await serialNumberService.GetListAsync(
            new SerialNumberQueryRequest(WorkOrderId: workOrderId, Page: 1, PageSize: 100),
            cancellationToken);

        if (serials.IsFailure)
        {
            return ApiResults.Problem(serials.Error);
        }

        var orderNumber = string.Empty;
        var workOrderResult = await workOrderService.GetByIdAsync(workOrderId, cancellationToken);
        if (workOrderResult.IsSuccess)
        {
            orderNumber = workOrderResult.Value.OrderNumber;
        }

        var nonconformances = await nonconformanceService.GetListAsync(
            new NonconformanceQueryRequest(WorkOrderId: workOrderId, Page: 1, PageSize: 1), cancellationToken);

        var inspections = await inspectionService.GetListAsync(
            new InspectionQueryRequest(WorkOrderId: workOrderId, Page: 1, PageSize: 100), cancellationToken);

        var items = serials.Value.Items;
        var inspectionItems = inspections.IsSuccess ? inspections.Value.Items : [];

        return Ok(new BatchTraceabilityReport(
            workOrderId,
            orderNumber,
            serials.Value.TotalCount,
            items.Count(s => s.Status == QiaoMES.Production.Domain.SerialNumberStatus.Completed),
            items.Count(s => s.Status == QiaoMES.Production.Domain.SerialNumberStatus.Scrapped),
            nonconformances.IsSuccess ? nonconformances.Value.TotalCount : 0,
            inspectionItems.Count(i => i.Status == QiaoMES.Quality.Domain.InspectionStatus.Failed),
            items));
    }
}
