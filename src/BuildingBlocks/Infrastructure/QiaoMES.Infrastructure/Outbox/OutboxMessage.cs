namespace QiaoMES.Infrastructure.Outbox;

/// <summary>Outbox 消息状态。</summary>
public enum OutboxStatus
{
    /// <summary>待投递（含等待重试）。</summary>
    Pending = 0,

    /// <summary>已成功投递。</summary>
    Succeeded = 1,

    /// <summary>超过重试上限，转人工处理。</summary>
    Failed = 2,
}

/// <summary>
/// Outbox 消息：集成事件的持久化载体。<para>
/// 写入与业务数据同一事务；投递由后台分发器异步完成，失败按指数退避重试，超限转 Failed 等待人工介入。
/// </para>
/// </summary>
public class OutboxMessage
{
    private OutboxMessage() { }

    public OutboxMessage(Guid id, string eventType, string payload, DateTime occurredAt)
    {
        Id = id;
        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
        CreatedAt = DateTime.UtcNow;
        Status = OutboxStatus.Pending;
        RetryCount = 0;
        NextRetryAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    /// <summary>事件类型全名（分发器据此路由到订阅者）。</summary>
    public string EventType { get; private set; } = string.Empty;

    /// <summary>事件负载（JSON）。</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>事件发生时间（业务时间，非落库时间）。</summary>
    public DateTime OccurredAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public OutboxStatus Status { get; private set; }

    public int RetryCount { get; private set; }

    public DateTime NextRetryAt { get; private set; }

    public DateTime? ProcessedAt { get; private set; }

    /// <summary>最后一次失败原因（截断保存）。</summary>
    public string? LastError { get; private set; }

    /// <summary>投递成功。</summary>
    public void MarkProcessed()
    {
        Status = OutboxStatus.Succeeded;
        ProcessedAt = DateTime.UtcNow;
        LastError = null;
    }

    /// <summary>投递失败：累计重试次数并按退避时间重排；达到上限转 <see cref="OutboxStatus.Failed"/>。</summary>
    public void MarkFailed(string error, int maxRetry, TimeSpan backoff)
    {
        RetryCount++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        NextRetryAt = DateTime.UtcNow.Add(backoff);
        Status = RetryCount >= maxRetry ? OutboxStatus.Failed : OutboxStatus.Pending;
    }
}
