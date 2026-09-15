using QiaoMES.Shared;

namespace QiaoMES.Quality.Domain;

/// <summary>来料批次状态。</summary>
public enum MaterialLotStatus
{
    /// <summary>待检（未做 IQC 或 IQC 未出结论）。</summary>
    Pending = 0,

    /// <summary>合格可用。</summary>
    Available = 1,

    /// <summary>判定不合格（禁止投产）。</summary>
    Rejected = 2,

    /// <summary>冻结（质量异常待查）。</summary>
    Frozen = 3,

    /// <summary>已耗尽。</summary>
    Depleted = 4,
}

/// <summary>
/// 来料批次（上游谱系起点）：一批来料的身份、供应商、数量与 IQC 结论。
/// </summary>
public class MaterialLot : Entity
{
    private MaterialLot() { }

    public MaterialLot(
        string lotNumber,
        string materialCode,
        decimal quantity,
        string? materialName = null,
        string? supplier = null,
        string? supplierLotNumber = null,
        string? unit = null,
        DateTime? receivedAt = null,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        LotNumber = lotNumber.Trim();
        MaterialCode = materialCode.Trim();
        Quantity = quantity;
        RemainingQuantity = quantity;
        MaterialName = materialName?.Trim();
        Supplier = supplier?.Trim();
        SupplierLotNumber = supplierLotNumber?.Trim();
        Unit = unit?.Trim();
        ReceivedAt = receivedAt ?? DateTime.UtcNow;
        Remark = remark?.Trim();
        Status = MaterialLotStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>批次号（供应商批次或内部批次）。</summary>
    public string LotNumber { get; private set; } = string.Empty;

    public string MaterialCode { get; private set; } = string.Empty;

    public string? MaterialName { get; private set; }

    public string? Supplier { get; private set; }

    /// <summary>供应商原始批号（便于对账）。</summary>
    public string? SupplierLotNumber { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>剩余可用数量。</summary>
    public decimal RemainingQuantity { get; private set; }

    public string? Unit { get; private set; }

    public DateTime ReceivedAt { get; private set; }

    public MaterialLotStatus Status { get; private set; }

    /// <summary>冻结 / 不合格原因。</summary>
    public string? StatusReason { get; private set; }

    /// <summary>关联的 IQC 检验单。</summary>
    public Guid? IqcInspectionId { get; private set; }

    public string? IqcInspectionNumber { get; private set; }

    /// <summary>IQC 判定时间。</summary>
    public DateTime? InspectedAt { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    /// <summary>是否允许投产（合格且有余量）。</summary>
    public bool CanConsume => Status == MaterialLotStatus.Available && RemainingQuantity > 0;

    /// <summary>登记 IQC 结论。</summary>
    public void MarkInspected(bool passed, Guid? inspectionId = null, string? inspectionNumber = null, string? reason = null)
    {
        IqcInspectionId = inspectionId ?? IqcInspectionId;
        IqcInspectionNumber = inspectionNumber ?? IqcInspectionNumber;
        InspectedAt = DateTime.UtcNow;
        Status = passed ? MaterialLotStatus.Available : MaterialLotStatus.Rejected;
        StatusReason = passed ? null : reason?.Trim() ?? "IQC 判定不合格";
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>冻结 / 解冻批次（质量异常待查时使用）。</summary>
    public Result SetFrozen(bool frozen, string? reason = null)
    {
        if (frozen)
        {
            if (Status == MaterialLotStatus.Depleted)
            {
                return Result.Failure(Error.Conflict("MaterialLot.Depleted", "批次已耗尽，无法冻结"));
            }

            Status = MaterialLotStatus.Frozen;
            StatusReason = reason?.Trim() ?? "批次冻结";
        }
        else
        {
            if (Status != MaterialLotStatus.Frozen)
            {
                return Result.Failure(Error.Conflict("MaterialLot.NotFrozen", "批次未处于冻结状态"));
            }

            Status = RemainingQuantity > 0 ? MaterialLotStatus.Available : MaterialLotStatus.Depleted;
            StatusReason = null;
        }

        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// 消耗批次数量（SN 绑定批次时调用）。批次不合格 / 冻结 / 余量不足时拒绝。
    /// </summary>
    public Result Consume(decimal quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(Error.Validation("MaterialLot.InvalidQuantity", "消耗数量必须大于 0"));
        }

        if (Status is MaterialLotStatus.Rejected or MaterialLotStatus.Frozen)
        {
            return Result.Failure(Error.Conflict(
                "MaterialLot.NotUsable",
                $"批次 {LotNumber} 当前状态为 {Status}，不允许投产"));
        }

        if (RemainingQuantity < quantity)
        {
            return Result.Failure(Error.Conflict(
                "MaterialLot.InsufficientQuantity",
                $"批次 {LotNumber} 剩余 {RemainingQuantity}，不足以消耗 {quantity}"));
        }

        // 允许「负库存」之外的严格扣减；为简化不做超额允许（MES 现场如需可后续加开关）
        RemainingQuantity -= quantity;
        if (RemainingQuantity <= 0)
        {
            Status = MaterialLotStatus.Depleted;
        }

        UpdatedAt = DateTime.UtcNow;
        return Result.Success();
    }
}

/// <summary>
/// SN 物料消耗绑定（下游谱系）：某颗 SN 在哪道工序用了哪一批来料。
/// </summary>
public class SnMaterialConsumption : Entity
{
    private SnMaterialConsumption() { }

    public SnMaterialConsumption(
        string sn,
        string materialCode,
        string lotNumber,
        decimal quantity,
        Guid? workOrderId = null,
        Guid? workOrderOperationId = null,
        string? operationName = null,
        string? equipmentCode = null,
        Guid? operatorId = null,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        Sn = sn.Trim();
        MaterialCode = materialCode.Trim();
        LotNumber = lotNumber.Trim();
        Quantity = quantity;
        WorkOrderId = workOrderId;
        WorkOrderOperationId = workOrderOperationId;
        OperationName = operationName?.Trim();
        EquipmentCode = equipmentCode?.Trim();
        OperatorId = operatorId;
        Remark = remark?.Trim();
        BoundAt = DateTime.UtcNow;
    }

    public string Sn { get; private set; } = string.Empty;

    public string MaterialCode { get; private set; } = string.Empty;

    public string LotNumber { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public Guid? WorkOrderOperationId { get; private set; }

    public string? OperationName { get; private set; }

    public string? EquipmentCode { get; private set; }

    public Guid? OperatorId { get; private set; }

    public DateTime BoundAt { get; private set; }

    public string? Remark { get; private set; }
}
