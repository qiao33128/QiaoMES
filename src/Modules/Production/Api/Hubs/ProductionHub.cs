using Microsoft.AspNetCore.SignalR;

namespace QiaoMES.Production.Api.Hubs;

/// <summary>
/// 生产看板实时通信 Hub。
/// 前端可通过 SignalR 订阅工单变更、看板数据等。
/// </summary>
public class ProductionHub : Hub
{
    public const string GroupAll = "all";

    public override async Task OnConnectedAsync()
    {
        // 新连接默认加入全员组，便于广播生产看板
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupAll);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 加入指定车间/产线分组，用于定向推送。
    /// </summary>
    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    /// <summary>
    /// 离开指定分组。
    /// </summary>
    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}
