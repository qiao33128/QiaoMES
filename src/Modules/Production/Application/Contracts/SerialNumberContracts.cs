using QiaoMES.Production.Domain;

namespace QiaoMES.Production.Application.Contracts;

/// <summary>批量生成 SN。</summary>
public record GenerateSerialNumbersRequest(Guid WorkOrderId, int Quantity, string? SnPrefix = null);

/// <summary>SN 查询请求。</summary>
public record SerialNumberQueryRequest(
    Guid? WorkOrderId = null,
    SerialNumberStatus? Status = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>进站请求。</summary>
public record SnTrackInRequest(Guid OperationTaskId, Guid? EquipmentId = null, string? Remark = null);

/// <summary>出站请求。</summary>
public record SnTrackOutRequest(
    Guid OperationTaskId,
    WipResult Result,
    Guid? EquipmentId = null,
    string? Remark = null);

/// <summary>SN 概要。</summary>
public record SerialNumberDto(
    Guid Id,
    string Sn,
    Guid WorkOrderId,
    string OrderNumber,
    Guid ProductId,
    string ProductCode,
    Guid? CurrentOperationTaskId,
    string? CurrentOperationName,
    string? LastCompletedOperationName,
    SerialNumberStatus Status,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>过站记录。</summary>
public record WipTrackingDto(
    Guid Id,
    Guid WorkOrderOperationId,
    string OperationName,
    WipAction Action,
    WipResult Result,
    Guid? OperatorId,
    Guid? EquipmentId,
    string? Remark,
    DateTime TrackedAt);

/// <summary>SN 详情（含过站轨迹）。</summary>
public record SerialNumberDetailDto(SerialNumberDto SerialNumber, IReadOnlyList<WipTrackingDto> Trackings);

/// <summary>
/// 生产统计快照（大屏 / 报表用）。<see cref="YieldRate"/> = 完工 / (完工 + 报废)。
/// </summary>
public record ProductionStatsDto(
    int Total,
    int InProcess,
    int Completed,
    int Scrapped,
    int OnHold,
    decimal YieldRate);
