namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单状态。
/// </summary>
public enum WorkOrderStatus
{
    /// <summary>草稿。</summary>
    Draft = 0,

    /// <summary>已下达。</summary>
    Released = 1,

    /// <summary>生产中。</summary>
    InProgress = 2,

    /// <summary>已完成。</summary>
    Completed = 3,

    /// <summary>已取消。</summary>
    Cancelled = 4,
}
