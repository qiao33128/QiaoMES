using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application.Contracts;

/// <summary>创建工单请求。</summary>
public record CreateWorkOrderRequest(
    string ProductCode,
    string ProductName,
    int PlannedQuantity,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? WorkCenter,
    string? Remark);

/// <summary>更新工单请求。</summary>
public record UpdateWorkOrderRequest(
    string ProductName,
    int PlannedQuantity,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? WorkCenter,
    string? Remark);

/// <summary>报工请求。</summary>
public record ReportRequest(int Quantity);

/// <summary>工单信息。</summary>
public record WorkOrderDto(
    Guid Id,
    string OrderNumber,
    string ProductCode,
    string ProductName,
    int PlannedQuantity,
    int CompletedQuantity,
    WorkOrderStatus Status,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? WorkCenter,
    string? Remark,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>分页工单列表。</summary>
public record WorkOrderListResult(PagedResult<WorkOrderDto> Paged, string? NextOrderNumberHint);
