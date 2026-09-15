using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

/// <summary>报工类型。</summary>
public enum ProductionReportType
{
    /// <summary>正常生产报工。</summary>
    Normal = 0,

    /// <summary>返工 / 返修报工（阶段 3 的维修闭环会用到）。</summary>
    Rework = 1,
}

/// <summary>
/// 工序报工记录（每次报工留痕，用于追溯与工时统计）。
/// </summary>
public class ProductionReport : Entity
{
    private ProductionReport() { }

    public ProductionReport(
        Guid workOrderId,
        Guid workOrderOperationId,
        int goodQuantity,
        int defectQuantity,
        int scrapQuantity,
        string? defectCode = null,
        Guid? operatorId = null,
        Guid? equipmentId = null,
        int workedSeconds = 0,
        ProductionReportType reportType = ProductionReportType.Normal,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        WorkOrderId = workOrderId;
        WorkOrderOperationId = workOrderOperationId;
        GoodQuantity = goodQuantity;
        DefectQuantity = defectQuantity;
        ScrapQuantity = scrapQuantity;
        DefectCode = defectCode?.Trim();
        OperatorId = operatorId;
        EquipmentId = equipmentId;
        WorkedSeconds = workedSeconds;
        ReportType = reportType;
        Remark = remark?.Trim();
        ReportedAt = DateTime.UtcNow;
    }

    public Guid WorkOrderId { get; private set; }

    /// <summary>对应的工序任务。</summary>
    public Guid WorkOrderOperationId { get; private set; }

    public int GoodQuantity { get; private set; }

    public int DefectQuantity { get; private set; }

    public int ScrapQuantity { get; private set; }

    /// <summary>不良代码（有不良品时填写）。</summary>
    public string? DefectCode { get; private set; }

    /// <summary>报工操作员。</summary>
    public Guid? OperatorId { get; private set; }

    /// <summary>执行设备（工作中心里类型为「设备」的条目）。</summary>
    public Guid? EquipmentId { get; private set; }

    /// <summary>本次实际工时（秒）。</summary>
    public int WorkedSeconds { get; private set; }

    /// <summary>正常报工 / 返工报工。</summary>
    public ProductionReportType ReportType { get; private set; }

    public string? Remark { get; private set; }

    public DateTime ReportedAt { get; private set; }

    /// <summary>本次报工总数。</summary>
    public int TotalQuantity => GoodQuantity + DefectQuantity + ScrapQuantity;
}
