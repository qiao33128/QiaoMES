using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

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

    public string? Remark { get; private set; }

    public DateTime ReportedAt { get; private set; }

    /// <summary>本次报工总数。</summary>
    public int TotalQuantity => GoodQuantity + DefectQuantity + ScrapQuantity;
}
