using QiaoMES.Equipment.Domain;

namespace QiaoMES.Equipment.Application.Contracts;

// ---------------- 设备 ----------------

public record EquipmentQueryRequest(
    string? Keyword = null,
    EquipmentStatus? Status = null,
    string? LineName = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20);

public record CreateEquipmentRequest(
    string Code,
    string Name,
    string? Model = null,
    string? SerialNumber = null,
    Guid? WorkCenterId = null,
    string? LineName = null,
    string? Remark = null);

public record UpdateEquipmentRequest(
    string Name,
    string? Model = null,
    string? SerialNumber = null,
    Guid? WorkCenterId = null,
    string? LineName = null,
    string? Remark = null);

public record ChangeEquipmentStatusRequest(
    EquipmentStatus Status,
    string? ReasonCode = null,
    string? Reason = null);

public record EquipmentStatusLogDto(
    Guid Id,
    EquipmentStatus FromStatus,
    EquipmentStatus ToStatus,
    string? ReasonCode,
    string? Reason,
    Guid? OperatorId,
    DateTime ChangedAt);

public record MaintenanceRecordDto(
    Guid Id,
    EquipmentMaintenanceType Type,
    string Content,
    EquipmentMaintenanceResult Result,
    string? AbnormalDescription,
    Guid? ExecutorId,
    DateTime ExecutedAt);

/// <summary>登记点检 / 保养 / 维修。</summary>
public record CreateMaintenanceRecordRequest(
    EquipmentMaintenanceType Type,
    string Content,
    EquipmentMaintenanceResult Result = EquipmentMaintenanceResult.Normal,
    string? AbnormalDescription = null);

public record EquipmentDto(
    Guid Id,
    string Code,
    string Name,
    string? Model,
    string? SerialNumber,
    Guid? WorkCenterId,
    string? LineName,
    EquipmentStatus Status,
    string? StatusReason,
    string? DownReasonCode,
    DateTime? StatusChangedAt,
    long TotalDownSeconds,
    long CurrentStatusSeconds,
    bool IsActive,
    string? Remark,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<EquipmentStatusLogDto> StatusLogs,
    IReadOnlyList<MaintenanceRecordDto> MaintenanceRecords);

/// <summary>设备状态汇总（Andon 看板红黄绿）。</summary>
public record EquipmentStatusSummaryDto(int Running, int Idle, int Down, int Maintenance, int Offline, int Total);

public record DowntimeParetoDto(string ReasonCode, int Count);

// ---------------- Andon ----------------

public record AndonQueryRequest(
    AndonStatus? Status = null,
    AndonType? Type = null,
    AndonLevel? Level = null,
    bool? OnlyOpen = null,
    string? Keyword = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20);

public record CreateAndonCallRequest(
    AndonType Type,
    string Description,
    AndonLevel Level = AndonLevel.Yellow,
    Guid? EquipmentId = null,
    string? EquipmentCode = null,
    Guid? WorkCenterId = null,
    string? WorkCenterName = null,
    string? Sn = null,
    int TimeoutMinutes = 10);

public record ResolveAndonRequest(string? Resolution = null);

public record AndonCallDto(
    Guid Id,
    string CallNumber,
    AndonType Type,
    AndonLevel Level,
    AndonStatus Status,
    Guid? EquipmentId,
    string? EquipmentCode,
    Guid? WorkCenterId,
    string? WorkCenterName,
    string? Sn,
    string Description,
    Guid? CallerId,
    DateTime CalledAt,
    int TimeoutMinutes,
    DateTime? RespondedAt,
    Guid? ResponderId,
    DateTime? ResolvedAt,
    string? Resolution,
    bool Escalated,
    DateTime? EscalatedAt,
    bool IsTimeout);
