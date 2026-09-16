using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Infrastructure.Outbox;

/// <summary>一条订阅登记：事件类型 → 处理器实现类型。</summary>
public sealed record OutboxSubscription(Type EventType, Type HandlerType);

/// <summary>
/// Outbox 分发处理器：取出待投递事件、反序列化、调用订阅者、标记结果。<para>
/// 与后台循环（<see cref="OutboxDispatcher"/>）分离，便于测试中确定性地手工触发一轮分发。
/// </para>
/// </summary>
public interface IOutboxProcessor
{
    /// <summary>投递一批待处理事件，返回本轮处理条数。</summary>
    Task<int> ProcessPendingAsync(int batchSize = 20, CancellationToken cancellationToken = default);
}

public sealed class OutboxProcessor(
    OutboxDbContext db,
    IServiceProvider serviceProvider,
    IEnumerable<OutboxSubscription> subscriptions,
    ILogger<OutboxProcessor> logger) : IOutboxProcessor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<int> ProcessPendingAsync(int batchSize = 20, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var take = batchSize is < 1 or > 200 ? 20 : batchSize;

        var messages = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending && m.NextRetryAt <= now)
            .OrderBy(m => m.OccurredAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        foreach (var message in messages)
        {
            // 无订阅者的事件视为已消费，避免堆积（订阅方按需接入）
            var matched = subscriptions.Where(s => s.EventType.FullName == message.EventType).ToList();
            if (matched.Count == 0)
            {
                message.MarkProcessed();
                continue;
            }

            try
            {
                // 同一事件可能被多个模块订阅：按订阅的事件类型分别反序列化并调用
                foreach (var subscription in matched)
                {
                    var integrationEvent = JsonSerializer.Deserialize(message.Payload, subscription.EventType, JsonOptions)
                        as IIntegrationEvent;
                    if (integrationEvent is null)
                    {
                        continue;
                    }

                    var handler = serviceProvider.GetRequiredService(subscription.HandlerType);
                    var method = subscription.HandlerType.GetMethod("HandleAsync");
                    if (method is null)
                    {
                        continue;
                    }

                    if (method.Invoke(handler, [integrationEvent, cancellationToken]) is Task task)
                    {
                        await task;
                    }
                }

                message.MarkProcessed();
            }
            catch (Exception exception)
            {
                // 指数退避：2s → 4s → … 上限 10 分钟；超过上限转 Failed 等待人工处理
                var backoff = TimeSpan.FromSeconds(Math.Min(600, Math.Pow(2, message.RetryCount + 1)));
                message.MarkFailed(exception.Message, OutboxDispatcher.MaxRetryCount, backoff);

                logger.LogWarning(
                    exception,
                    "集成事件投递失败（第 {Retry} 次）EventType={EventType} EventId={EventId}",
                    message.RetryCount,
                    message.EventType,
                    message.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }
}
