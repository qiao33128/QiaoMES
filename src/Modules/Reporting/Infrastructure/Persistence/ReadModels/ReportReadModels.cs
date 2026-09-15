namespace QiaoMES.Reporting.Infrastructure.Persistence.ReadModels;

/// <summary>
/// 报表只读投影（read model）。<para>
/// 报表模块作为「读模型」直接映射其它模块的表做 SQL 级聚合，避免把海量明细拉到内存再算；
/// 这些实体全部 <c>ExcludeFromMigrations</c>，不参与本模块迁移，写操作一律由各业务模块负责。
/// </para>
/// </summary>
public class WorkOrderReadModel
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; }
    public int CompletedQuantity { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PlannedStart { get; set; }
}

public class WorkOrderOperationReadModel
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public string OperationName { get; set; } = string.Empty;
    public int StandardSeconds { get; set; }
    public int ActualSeconds { get; set; }
    public int PlannedQuantity { get; set; }
    public int GoodQuantity { get; set; }
    public int DefectQuantity { get; set; }
    public int ScrapQuantity { get; set; }
    public int Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class SerialNumberReadModel
{
    public Guid Id { get; set; }
    public string Sn { get; set; } = string.Empty;
    public Guid WorkOrderId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EquipmentStatusLogReadModel
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public int FromStatus { get; set; }
    public int ToStatus { get; set; }
    public string? ReasonCode { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class InspectionReadModel
{
    public Guid Id { get; set; }
    public string InspectionNumber { get; set; } = string.Empty;
    public int Type { get; set; }
    public int Status { get; set; }
    public string? Sn { get; set; }
    public string? ProductCode { get; set; }
    public int DefectQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>检验项只读投影（不良代码 TOP N）。</summary>
public class InspectionItemReadModel
{
    public Guid Id { get; set; }
    public Guid InspectionId { get; set; }
    public string? DefectCode { get; set; }
    public bool? IsQualified { get; set; }
}
