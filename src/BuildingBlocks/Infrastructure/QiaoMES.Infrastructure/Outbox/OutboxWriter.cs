using System.Text.Json;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Infrastructure.Outbox;

/// <summary>
/// Outbox 写入器：只把事件追加到 Outbox 表，不调用 SaveChanges ——
/// 由工作单元在业务事务提交时一并落库，保证「事件与业务同生共死」。
/// </summary>
public sealed class OutboxWriter(OutboxDbContext db) : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Publish(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = integrationEvent.GetType();
        var payload = JsonSerializer.Serialize(integrationEvent, eventType, JsonOptions);

        db.OutboxMessages.Add(new OutboxMessage(
            integrationEvent.EventId,
            eventType.FullName ?? eventType.Name,
            payload,
            integrationEvent.OccurredAt));
    }
}
