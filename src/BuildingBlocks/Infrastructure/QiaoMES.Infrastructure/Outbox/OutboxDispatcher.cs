using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QiaoMES.Infrastructure.Outbox;

/// <summary>
/// Outbox 后台分发器：周期性驱动 <see cref="IOutboxProcessor"/> 投递待发事件。<para>
/// 每个周期使用独立作用域（不能用请求作用域），保证长驻后台任务不持有过期的 DbContext。
/// </para>
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    /// <summary>单个事件的最大投递次数（超过转 Failed）。</summary>
    public const int MaxRetryCount = 8;

    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Outbox 分发器已启动（轮询间隔 {Interval} 秒）", IdleInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();
                var processed = await processor.ProcessPendingAsync(50, stoppingToken);

                // 有积压时立即继续下一轮，空闲时才等待，兼顾吞吐与空转开销
                if (processed == 0)
                {
                    await Task.Delay(IdleInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Outbox 分发循环异常，{Interval} 秒后重试", IdleInterval.TotalSeconds);
                try
                {
                    await Task.Delay(IdleInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        logger.LogInformation("Outbox 分发器已停止");
    }
}
