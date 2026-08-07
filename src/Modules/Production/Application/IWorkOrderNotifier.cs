using QiaoMES.Production.Application.Contracts;

namespace QiaoMES.Production.Application;

/// <summary>
/// 工单实时通知器（由 API 层通过 SignalR 实现）。
/// </summary>
public interface IWorkOrderNotifier
{
    /// <summary>广播工单变更事件到生产看板。</summary>
    Task NotifyWorkOrderChangedAsync(WorkOrderDto workOrder, string action, CancellationToken cancellationToken = default);
}
