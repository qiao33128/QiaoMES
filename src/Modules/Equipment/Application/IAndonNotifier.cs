using QiaoMES.Equipment.Domain;

namespace QiaoMES.Equipment.Application;

/// <summary>
/// Andon 实时通知抽象（由 API 层用 SignalR 实现，推送到车间看板）。
/// </summary>
public interface IAndonNotifier
{
    /// <summary>新呼叫产生。</summary>
    Task NotifyCalledAsync(AndonCall call, CancellationToken cancellationToken = default);

    /// <summary>呼叫被响应。</summary>
    Task NotifyRespondedAsync(AndonCall call, CancellationToken cancellationToken = default);

    /// <summary>呼叫已解决 / 关闭。</summary>
    Task NotifyResolvedAsync(AndonCall call, CancellationToken cancellationToken = default);

    /// <summary>呼叫超时升级为红灯。</summary>
    Task NotifyEscalatedAsync(AndonCall call, CancellationToken cancellationToken = default);
}
