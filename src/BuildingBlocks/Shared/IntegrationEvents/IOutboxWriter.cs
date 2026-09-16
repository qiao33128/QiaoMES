namespace QiaoMES.Shared.IntegrationEvents;

/// <summary>
/// 集成事件发布（Outbox 写入）。<para>
/// 调用时只把事件写进 Outbox 表，**与业务数据同一个事务**一起提交（由工作单元统一 SaveChanges + Commit），
/// 再由后台分发器异步投递。这样「业务成功但事件丢失」和「事件发出但业务回滚」都不会发生。
/// </para>
/// </summary>
public interface IOutboxWriter
{
    /// <summary>登记一个待投递的集成事件（在事务内落库，不立即投递）。</summary>
    void Publish(IIntegrationEvent integrationEvent);
}
