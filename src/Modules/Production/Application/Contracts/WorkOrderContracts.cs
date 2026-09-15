using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application.Contracts;

/// <summary>创建工单请求。产品必须来自主数据（会校验存在与启用）。</summary>
public record CreateWorkOrderRequest(
    Guid ProductId,
    int PlannedQuantity,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? WorkCenter,
    string? Remark);

/// <summary>更新工单请求（仅草稿状态可改）。</summary>
public record UpdateWorkOrderRequest(
    Guid ProductId,
    int PlannedQuantity,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? WorkCenter,
    string? Remark);

/// <summary>工序报工请求。</summary>
public record ReportOperationRequest(
    int GoodQuantity,
    int DefectQuantity = 0,
    int ScrapQuantity = 0,
    string? DefectCode = null,
    string? Remark = null);

/// <summary>工单的一道工序任务。</summary>
public record WorkOrderOperationDto(
    Guid Id,
    int Sequence,
    Guid OperationId,
    string OperationCode,
    string OperationName,
    Guid? WorkCenterId,
    int StandardSeconds,
    bool IsQualityGate,
    int PlannedQuantity,
    int GoodQuantity,
    int DefectQuantity,
    int ScrapQuantity,
    int ReportedQuantity,
    int ProgressPercent,
    WorkOrderOperationStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt);

/// <summary>工单信息。</summary>
public record WorkOrderDto(
    Guid Id,
    string OrderNumber,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    int PlannedQuantity,
    int CompletedQuantity,
    WorkOrderStatus Status,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    string? WorkCenter,
    string? Remark,
    Guid? RoutingId,
    string? RoutingVersion,
    Guid? BomId,
    string? BomVersion,
    IReadOnlyList<WorkOrderOperationDto> Operations,
    DateTime CreatedAt,
    DateTime? CompletedAt);
