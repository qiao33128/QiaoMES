using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

/// <summary>
/// 生产报工记录。
/// </summary>
public class ProductionReport : Entity
{
    private ProductionReport() { }

    public ProductionReport(Guid workOrderId, int quantity, DateTime reportedAt)
    {
        WorkOrderId = workOrderId;
        Quantity = quantity;
        ReportedAt = reportedAt;
    }

    public Guid WorkOrderId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime ReportedAt { get; private set; }
}
