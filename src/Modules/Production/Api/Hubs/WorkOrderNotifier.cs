using Microsoft.AspNetCore.SignalR;
using QiaoMES.Production.Application;
using QiaoMES.Production.Application.Contracts;

namespace QiaoMES.Production.Api.Hubs;

public class WorkOrderNotifier(IHubContext<ProductionHub> hubContext) : IWorkOrderNotifier
{
    public async Task NotifyWorkOrderChangedAsync(WorkOrderDto workOrder, string action, CancellationToken cancellationToken = default)
    {
        await hubContext.Clients
            .Group(ProductionHub.GroupAll)
            .SendAsync("WorkOrderChanged", new WorkOrderChangedEvent(action, workOrder), cancellationToken);
    }
}

public record WorkOrderChangedEvent(string Action, WorkOrderDto WorkOrder);
