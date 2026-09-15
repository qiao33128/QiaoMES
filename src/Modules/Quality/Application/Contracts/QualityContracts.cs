using QiaoMES.Quality.Domain;

namespace QiaoMES.Quality.Application.Contracts;

// ---------------- 检验单 ----------------

public record InspectionQueryRequest(
    InspectionType? Type = null,
    InspectionStatus? Status = null,
    string? Keyword = null,
    Guid? WorkOrderId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>检验项定义。</summary>
public record InspectionItemRequest(
    string Name,
    string? Standard = null,
    decimal? LowerLimit = null,
    decimal? UpperLimit = null,
    bool IsKeyItem = false,
    string? Remark = null);

public record CreateInspectionRequest(
    InspectionType Type,
    int SampleSize,
    Guid? WorkOrderId = null,
    Guid? WorkOrderOperationId = null,
    string? Sn = null,
    Guid? MaterialId = null,
    string? MaterialCode = null,
    string? ProductCode = null,
    string? AqlLevel = null,
    int AcceptedLimit = 0,
    int RejectedLimit = 1,
    IReadOnlyList<InspectionItemRequest>? Items = null);

public record AddInspectionItemsRequest(IReadOnlyList<InspectionItemRequest> Items);

public record RecordInspectionItemRequest(
    Guid ItemId,
    string? MeasuredValue = null,
    decimal? NumericValue = null,
    bool? IsQualified = null,
    string? DefectCode = null,
    string? Remark = null);

/// <summary>提交判定。</summary>
public record SubmitInspectionRequest(
    int DefectQuantity = 0,
    bool Concession = false,
    string? Remark = null,
    /// <summary>判定不合格时是否自动生成不合格处置单。</summary>
    bool CreateNonconformance = false,
    string? DefectDescription = null);

public record InspectionItemDto(
    Guid Id,
    int Sequence,
    string Name,
    string? Standard,
    decimal? LowerLimit,
    decimal? UpperLimit,
    bool IsKeyItem,
    string? MeasuredValue,
    decimal? NumericValue,
    bool? IsQualified,
    string? DefectCode,
    string? Remark);

public record InspectionDto(
    Guid Id,
    string InspectionNumber,
    InspectionType Type,
    InspectionStatus Status,
    InspectionConclusion Conclusion,
    Guid? WorkOrderId,
    Guid? WorkOrderOperationId,
    string? Sn,
    Guid? MaterialId,
    string? MaterialCode,
    string? ProductCode,
    int SampleSize,
    string? AqlLevel,
    int AcceptedLimit,
    int RejectedLimit,
    int DefectQuantity,
    Guid? InspectorId,
    string? InspectorName,
    DateTime CreatedAt,
    DateTime? InspectedAt,
    string? Remark,
    int FailedItemCount,
    bool IsFullyRecorded,
    IReadOnlyList<InspectionItemDto> Items);

// ---------------- 不合格处置 ----------------

public record NonconformanceQueryRequest(
    DispositionStatus? Status = null,
    DispositionType? Disposition = null,
    string? Keyword = null,
    Guid? WorkOrderId = null,
    int Page = 1,
    int PageSize = 20);

public record CreateNonconformanceRequest(
    int Quantity = 1,
    Guid? InspectionId = null,
    Guid? WorkOrderId = null,
    string? Sn = null,
    string? DefectCode = null,
    string? DefectDescription = null,
    string? ProductCode = null);

public record DecideNonconformanceRequest(DispositionType Disposition, bool NeedReinspect = true, string? Remark = null);

public record StartRepairRequest(string Description, string? Remark = null);

public record CompleteRepairRequest(Guid RepairId, string? Result = null, string? Remark = null);

public record ReinspectResultRequest(bool Passed, Guid? ReinspectionId = null, string? Remark = null);

public record RepairRecordDto(
    Guid Id,
    string Description,
    string? Result,
    Guid? RepairerId,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? Remark);

public record NonconformanceDto(
    Guid Id,
    string NonconformanceNumber,
    Guid? InspectionId,
    Guid? WorkOrderId,
    string? Sn,
    string? ProductCode,
    string? DefectCode,
    string? DefectDescription,
    int Quantity,
    DispositionType? Disposition,
    DispositionStatus Status,
    bool NeedReinspect,
    Guid? HandlerId,
    Guid? ReinspectionId,
    DateTime CreatedAt,
    DateTime? DecidedAt,
    DateTime? ClosedAt,
    string? Remark,
    IReadOnlyList<RepairRecordDto> Repairs);

// ---------------- 不良代码 ----------------

public record DefectCodeQueryRequest(
    string? Keyword = null,
    string? Category = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20);

public record CreateDefectCodeRequest(string Code, string Name, string? Category = null, string? Description = null);

public record UpdateDefectCodeRequest(string Name, string? Category = null, string? Description = null);

public record DefectCodeDto(
    Guid Id,
    string Code,
    string Name,
    string? Category,
    string? Description,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record DefectParetoDto(string DefectCode, int Count);

// ---------------- SPC ----------------

public record SpcTrendRequest(string ItemName, DateTime? From = null, DateTime? To = null, int Points = 30);

public record SpcPointDto(DateTime Timestamp, decimal? Value, bool? IsQualified, string InspectionNumber);

public record SpcTrendDto(
    string ItemName,
    decimal? LowerLimit,
    decimal? UpperLimit,
    decimal? Mean,
    decimal? StdDev,
    decimal? UpperControlLimit,
    decimal? LowerControlLimit,
    int SampleCount,
    bool HasSignal,
    string? SignalDescription,
    IReadOnlyList<SpcPointDto> Points);
