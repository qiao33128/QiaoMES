using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Outbox;
using QiaoMES.Reporting.Infrastructure.Persistence;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>Outbox 投递健康度。</summary>
/// <param name="Pending">待投递（含等待重试）。</param>
/// <param name="Failed">已超重试上限，需人工介入。</param>
/// <param name="OldestPendingAgeSeconds">最老待投递事件已经积压多久（秒）。</param>
public record OutboxHealthDto(
    int Pending,
    int Failed,
    int SucceededLast24h,
    DateTime? OldestPendingAt,
    double? OldestPendingAgeSeconds);

/// <summary>预聚合新鲜度（看板/报表数据是否及时）。</summary>
public record MetricsFreshnessDto(DateTime? LastComputedAt, double? AgeMinutes, int RowsInLast7Days);

/// <summary>数据库运行指标。</summary>
public record DatabaseHealthDto(int ActiveConnections, string DatabaseSize, long DatabaseSizeBytes);

/// <summary>
/// 运维可观测性快照（阶段 5.6）。<para>
/// 刻意保持**零外部依赖**：不引入 OTel Collector / Prometheus，先用最少的接口暴露「系统是否健康」的信息，
/// 便于在没有任何监控设施的环境下也能排障；接入标准可观测栈时可直接复用这里的分区与指标定义。
/// </para>
/// </summary>
[ApiController]
[Route("api/monitoring")]
[Authorize]
public class MonitoringController(
    OutboxDbContext outboxDb,
    ReportingDbContext reportingDb,
    DbConnection connection) : ControllerBase
{
    /// <summary>Outbox 投递健康度（积压告警的核心指标）。</summary>
    [HttpGet("outbox")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetOutboxHealth(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var since = now.AddHours(-24);

        var pending = await outboxDb.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Pending, cancellationToken);
        var failed = await outboxDb.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Failed, cancellationToken);
        var succeeded = await outboxDb.OutboxMessages.CountAsync(m => m.Status == OutboxStatus.Succeeded && m.ProcessedAt >= since, cancellationToken);
        var oldest = await outboxDb.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending)
            .OrderBy(m => m.OccurredAt)
            .Select(m => (DateTime?)m.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new OutboxHealthDto(
            pending,
            failed,
            succeeded,
            oldest,
            oldest is null ? null : Math.Round((now - oldest.Value).TotalSeconds, 1)));
    }

    /// <summary>预聚合新鲜度：最后一次重算时间与近 7 天汇总行数。</summary>
    [HttpGet("metrics-freshness")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetMetricsFreshness(CancellationToken cancellationToken)
    {
        var lastComputedAt = await reportingDb.DailyShiftMetrics
            .OrderByDescending(m => m.ComputedAt)
            .Select(m => (DateTime?)m.ComputedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var rows = await reportingDb.DailyShiftMetrics
            .CountAsync(m => m.ProductionDate >= today.AddDays(-6) && m.ProductionDate <= today, cancellationToken);

        return Ok(new MetricsFreshnessDto(
            lastComputedAt,
            lastComputedAt is null ? null : Math.Round((DateTime.UtcNow - lastComputedAt.Value).TotalMinutes, 1),
            rows));
    }

    /// <summary>数据库连接数与库体积。</summary>
    [HttpGet("database")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetDatabaseHealth(CancellationToken cancellationToken)
    {
        var activeConnections = Convert.ToInt32(await ScalarAsync(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()", cancellationToken));

        var sizeBytes = Convert.ToInt64(await ScalarAsync("SELECT pg_database_size(current_database())", cancellationToken));
        var prettySize = (await ScalarAsync("SELECT pg_size_pretty(pg_database_size(current_database()))", cancellationToken))?.ToString()
            ?? $"{sizeBytes} bytes";

        return Ok(new DatabaseHealthDto(activeConnections, prettySize, sizeBytes));
    }

    /// <summary>
    /// 汇总快照：一次拿到全部健康指标，便于脚本化巡检与告警规则编写。<para>
    /// 建议告警阈值：Outbox <c>Pending &gt; 100</c> 或 <c>OldestPendingAgeSeconds &gt; 300</c> 或 <c>Failed &gt; 0</c>；
    /// 预聚合 <c>AgeMinutes &gt; 15</c>。
    /// </para>
    /// </summary>
    [HttpGet("snapshot")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetSnapshot(CancellationToken cancellationToken)
    {
        var outbox = await GetOutboxHealth(cancellationToken) as OkObjectResult;
        var freshness = await GetMetricsFreshness(cancellationToken) as OkObjectResult;
        var database = await GetDatabaseHealth(cancellationToken) as OkObjectResult;

        var body = new
        {
            generatedAt = DateTime.UtcNow,
            outbox = outbox?.Value,
            metricsFreshness = freshness?.Value,
            database = database?.Value,
            thresholds = new
            {
                outboxPending = 100,
                outboxOldestAgeSeconds = 300,
                outboxFailed = 0,
                metricsAgeMinutes = 15,
            },
        };

        return Ok(body);
    }

    private async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }
}
