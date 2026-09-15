using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using QiaoMES.Equipment.Application;
using QiaoMES.Equipment.Domain;

namespace QiaoMES.Equipment.Api.Hubs;

/// <summary>
/// Andon 实时看板通道：车间大屏订阅本 Hub 获取红黄绿呼叫推送。
/// </summary>
[Authorize]
public class AndonHub : Hub;

/// <summary>
/// 基于 SignalR 的 Andon 通知实现。
/// </summary>
public sealed class AndonNotifier(IHubContext<AndonHub> hub) : IAndonNotifier
{
    public Task NotifyCalledAsync(AndonCall call, CancellationToken cancellationToken = default)
        => BroadcastAsync("called", call, cancellationToken);

    public Task NotifyRespondedAsync(AndonCall call, CancellationToken cancellationToken = default)
        => BroadcastAsync("responded", call, cancellationToken);

    public Task NotifyResolvedAsync(AndonCall call, CancellationToken cancellationToken = default)
        => BroadcastAsync("resolved", call, cancellationToken);

    public Task NotifyEscalatedAsync(AndonCall call, CancellationToken cancellationToken = default)
        => BroadcastAsync("escalated", call, cancellationToken);

    private Task BroadcastAsync(string action, AndonCall call, CancellationToken cancellationToken)
        => hub.Clients.All.SendAsync(
            "andon-updated",
            new
            {
                action,
                call.Id,
                call.CallNumber,
                type = (int)call.Type,
                level = (int)call.Level,
                status = (int)call.Status,
                call.EquipmentCode,
                call.WorkCenterName,
                call.Sn,
                call.Description,
                call.CalledAt,
                call.RespondedAt,
                call.ResolvedAt,
                call.Escalated,
                call.EscalatedAt,
            },
            cancellationToken);
}
