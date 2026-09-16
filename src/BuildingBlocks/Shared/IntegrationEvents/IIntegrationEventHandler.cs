namespace QiaoMES.Shared.IntegrationEvents;

/// <summary>
/// 集成事件处理器（由订阅方实现）。<para>
/// 处理器由 Outbox 后台分发器调用，运行在**独立的作用域**中，因此不能依赖请求级上下文
/// （HttpContext / ICurrentUser）；需要「谁触发的」时请使用事件负载里携带的操作人。
/// </para>
/// <para>
/// 幂等要求：Outbox 至少投递一次，处理器必须对同一 <c>EventId</c> 重复执行安全。
/// </para>
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
