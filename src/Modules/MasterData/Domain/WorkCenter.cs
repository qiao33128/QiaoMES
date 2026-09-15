namespace QiaoMES.MasterData.Domain;

/// <summary>工作中心类型。</summary>
public enum WorkCenterType
{
    /// <summary>产线。</summary>
    Line = 0,

    /// <summary>生产单元 / 班组区域。</summary>
    Cell = 1,

    /// <summary>工位。</summary>
    Station = 2,

    /// <summary>单台设备。</summary>
    Machine = 3,
}

/// <summary>
/// 工作中心：产线 / 单元 / 工位 / 设备。工序与工单都挂在工作中心上。
/// </summary>
public class WorkCenter : CatalogEntity
{
    private WorkCenter() { }

    public WorkCenter(
        string code,
        string name,
        WorkCenterType type = WorkCenterType.Line,
        string? workshop = null,
        string? remark = null)
        : base(code, name, null, null, remark)
    {
        Type = type;
        Workshop = workshop?.Trim();
    }

    public WorkCenterType Type { get; private set; }

    /// <summary>所属车间 / 区域。</summary>
    public string? Workshop { get; private set; }

    /// <summary>上级工作中心（工位挂在产线下时可指定）。</summary>
    public Guid? ParentId { get; private set; }

    public void UpdateLayout(WorkCenterType type, string? workshop, Guid? parentId)
    {
        Type = type;
        Workshop = workshop?.Trim();
        ParentId = parentId;
        UpdatedAt = DateTime.UtcNow;
    }
}
