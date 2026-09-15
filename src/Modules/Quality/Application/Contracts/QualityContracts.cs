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
    /// <summary>来料批次号（IQC 时填写；判定后自动回写批次状态）。</summary>
    string? LotNumber = null,
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
    string? LotNumber,
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

// ---------------- 来料批次与谱系 ----------------

public record MaterialLotQueryRequest(
    string? Keyword = null,
    string? MaterialCode = null,
    MaterialLotStatus? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20);

public record CreateMaterialLotRequest(
    string LotNumber,
    string MaterialCode,
    decimal Quantity,
    string? MaterialName = null,
    string? Supplier = null,
    string? SupplierLotNumber = null,
    string? Unit = null,
    DateTime? ReceivedAt = null,
    string? Remark = null);

/// <summary>IQC 结论回写批次状态。</summary>
public record InspectMaterialLotRequest(
    bool Passed,
    Guid? InspectionId = null,
    string? InspectionNumber = null,
    string? Reason = null);

public record FreezeMaterialLotRequest(bool Frozen, string? Reason = null);

public record MaterialLotDto(
    Guid Id,
    string LotNumber,
    string MaterialCode,
    string? MaterialName,
    string? Supplier,
    string? SupplierLotNumber,
    decimal Quantity,
    decimal RemainingQuantity,
    string? Unit,
    DateTime ReceivedAt,
    MaterialLotStatus Status,
    string? StatusReason,
    Guid? IqcInspectionId,
    string? IqcInspectionNumber,
    DateTime? InspectedAt,
    string? Remark,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int ConsumedSnCount,
    decimal ConsumedQuantity);

/// <summary>SN 绑定来料批次（下游谱系写入）。</summary>
public record BindMaterialConsumptionRequest(
    string Sn,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    Guid? WorkOrderId = null,
    Guid? WorkOrderOperationId = null,
    string? OperationName = null,
    string? EquipmentCode = null,
    string? Remark = null);

public record BindMaterialConsumptionsRequest(IReadOnlyList<BindMaterialConsumptionRequest> Items);

public record SnMaterialConsumptionDto(
    Guid Id,
    string Sn,
    string MaterialCode,
    string LotNumber,
    decimal Quantity,
    Guid? WorkOrderId,
    Guid? WorkOrderOperationId,
    string? OperationName,
    string? EquipmentCode,
    Guid? OperatorId,
    DateTime BoundAt,
    string? Remark);

/// <summary>批次流向汇总（反向追溯：来料批次 → 受影响 SN 集合）。</summary>
public record MaterialLotTraceDto(
    MaterialLotDto Lot,
    int SnCount,
    decimal ConsumedQuantity,
    IReadOnlyList<SnMaterialConsumptionDto> Consumptions);

// ---------------- 指标统计（大屏 / 报表） ----------------

/// <summary>
/// 质量统计快照。<see cref="Fpy"/> 为一次合格率 = 合格单 / 已判定单（让步接收计为未一次合格）。
/// </summary>
public record QualityStatsDto(
    int Total,
    int Pending,
    int Passed,
    int Failed,
    int Concessioned,
    int DefectQuantity,
    decimal Fpy);
