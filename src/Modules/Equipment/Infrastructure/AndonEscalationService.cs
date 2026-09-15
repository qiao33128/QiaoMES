using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QiaoMES.Equipment.Application;
using QiaoMES.Equipment.Domain;

namespace QiaoMES.Equipment.Infrastructure;

/// <summary>
/// 后台服务：定时扫描「超时未响应」的 Andon 呼叫，自动升级为红灯并推送到车间看板。
/// </summary>
public sealed class AndonEscalationService(
    IServiceScopeFactory scopeFactory,
    ILogger<AndonEscalationService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 应用关闭，正常退出
            }
            catch (Exception exception)
            {
                // 后台任务不能让异常冒泡终止整个服务
                logger.LogError(exception, "Andon 超时升级扫描失败");
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

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAndonRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<IAndonNotifier>();

        var now = DateTime.UtcNow;
        var calls = await repository.GetTimeoutCallsAsync(now, cancellationToken);
        if (calls.Count == 0)
        {
            return;
        }

        var escalated = calls.Where(call => call.Escalate(now)).ToList();
        if (escalated.Count == 0)
        {
            return;
        }

        await repository.SaveChangesAsync(cancellationToken);

        foreach (var call in escalated)
        {
            logger.LogWarning(
                "Andon 呼叫 {CallNumber} 超过 {TimeoutMinutes} 分钟未响应，已自动升级为红灯",
                call.CallNumber,
                call.TimeoutMinutes);

            await notifier.NotifyEscalatedAsync(call, cancellationToken);
        }
    }
}
