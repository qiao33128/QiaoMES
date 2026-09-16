using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QiaoMES.Reporting.Infrastructure;

/// <summary>
/// 预聚合滚动重算：周期性把「昨天 + 今天」的生产日重算进 <c>daily_shift_metrics</c>。<para>
/// 覆盖昨天是因为跨天夜班的生产日归属开始日；只重算近两天，保证重算成本恒定、与历史数据量无关。
/// 历史日期由手动重算接口（`POST /api/reports/rebuild-metrics`）补齐。
/// </para>
/// </summary>
public sealed class MetricsAggregationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MetricsAggregationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>启动后先等待，避免与迁移 / 种子数据抢数据库。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var aggregator = scope.ServiceProvider.GetRequiredService<IMetricsAggregator>();

                var today = DateOnly.FromDateTime(DateTime.Now);
                var shifts = await aggregator.RebuildAsync(today.AddDays(-1), today, null, stoppingToken);

                logger.LogDebug("预聚合完成：生产日 {From} ~ {To}，共 {Shifts} 个班次", today.AddDays(-1), today, shifts);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "预聚合重算失败，将在下个周期重试");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
