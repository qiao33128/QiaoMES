using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>
/// 性能诊断与压测（**仅 Development 环境可用**）。<para>
/// 用途：① 索引健康体检（全表扫描大户 / 从未命中的索引 / 表与索引体积）；
/// ② 关键查询执行计划（EXPLAIN ANALYZE）；③ 造种子数据；④ 并发基准测试产出 P50 / P95 / P99。
/// </para>
/// </summary>
[ApiController]
[Route("api/performance")]
[Authorize]
public class PerformanceController(
    DbConnection connection,
    IConfiguration configuration,
    IHostEnvironment environment) : ControllerBase
{
    /// <summary>压测必须用独立连接：共享的 scoped 连接在并发下会抛 NpgsqlOperationInProgressException。</summary>
    private string ConnectionString => configuration.GetConnectionString("DefaultDb")
        ?? throw new InvalidOperationException("未配置 ConnectionStrings:DefaultDb");

    /// <summary>关键查询场景（与代码中的真实查询路径一一对应）。</summary>
    private static readonly Dictionary<string, (string Description, string Sql, string DefaultArg)> Scenarios = new()
    {
        ["sn-lookup"] = (
            "按 SN 查详情（过站追溯入口，23 亿行级表的等价场景）",
            """SELECT * FROM production.serial_numbers WHERE "Sn" = @arg""",
            "SN-PERF-000000001"),
        ["sn-by-workorder"] = (
            "按工单分页取 SN（看板/列表）",
            """SELECT * FROM production.serial_numbers WHERE "WorkOrderId" = @arg::uuid ORDER BY "CreatedAt" DESC LIMIT 20""",
            ""),
        ["inspection-by-sn"] = (
            "按 SN 取检验历史（追溯报告）",
            """SELECT * FROM quality.inspections WHERE "Sn" = @arg ORDER BY "CreatedAt" DESC LIMIT 50""",
            "SN-PERF-000000001"),
        ["inspection-daily"] = (
            "按时间区间统计检验单（报表口径）",
            """SELECT "Status", count(*), sum("DefectQuantity") FROM quality.inspections WHERE "CreatedAt" >= now() - interval '30 days' GROUP BY "Status" """,
            ""),
        ["sn-status-stats"] = (
            "SN 状态分布统计（OEE / 大屏）",
            """SELECT "Status", count(*) FROM production.serial_numbers WHERE "CreatedAt" >= now() - interval '7 days' GROUP BY "Status" """,
            ""),
        ["outbox-pending"] = (
            "Outbox 待投递扫描（分发器每 5 秒执行）",
            """SELECT * FROM infrastructure.outbox_messages WHERE "Status" = 0 AND "NextRetryAt" <= now() ORDER BY "OccurredAt" LIMIT 50""",
            ""),
        ["andon-open"] = (
            "未结束 Andon 呼叫（大屏轮播）",
            """SELECT * FROM equipment.andon_calls WHERE "Status" IN (0, 1) ORDER BY "CallNumber" DESC LIMIT 20""",
            ""),
    };

    /// <summary>当前环境是否允许调用（防止生产被压测）。</summary>
    private IActionResult? Guard()
        => environment.IsDevelopment()
            ? null
            : ApiResults.Problem(new QiaoMES.Shared.Error("Performance.Disabled", "性能端点仅在开发环境可用", QiaoMES.Shared.ErrorType.Forbidden));

    /// <summary>可用的压测场景清单。</summary>
    [HttpGet("scenarios")]
    [HasPermission(Permissions.Reporting.Read)]
    public IActionResult GetScenarios()
        => Guard() ?? Ok(Scenarios.Select(pair => new
        {
            key = pair.Key,
            description = pair.Value.Description,
            sql = pair.Value.Sql,
        }));

    /// <summary>查看某张表的列定义（造种子数据前确认必填列）。</summary>
    [HttpGet("schema")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetSchema([FromQuery] string table, CancellationToken cancellationToken)
        => Guard() ?? Ok(await QueryAsync(
            """
            SELECT table_schema, table_name, column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema || '.' || table_name = @arg
            ORDER BY ordinal_position
            """,
            table,
            cancellationToken));

    /// <summary>
    /// 索引健康体检：顺序扫描大户（缺索引嫌疑）、从未被使用的索引（写放大）、表与索引体积。
    /// </summary>
    [HttpGet("index-health")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> IndexHealth(CancellationToken cancellationToken)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        var tables = await QueryAsync(
            """
            SELECT schemaname AS schema, relname AS table, seq_scan, idx_scan,
                   n_live_tup AS live_rows,
                   pg_size_pretty(pg_total_relation_size(relid)) AS total_size
            FROM pg_stat_user_tables
            WHERE schemaname IN ('production', 'quality', 'equipment', 'reporting', 'infrastructure', 'integration')
            ORDER BY seq_scan DESC NULLS LAST
            LIMIT 25
            """,
            null,
            cancellationToken);

        var unusedIndexes = await QueryAsync(
            """
            SELECT schemaname AS schema, relname AS table, indexrelname AS index, idx_scan,
                   pg_size_pretty(pg_relation_size(indexrelid)) AS index_size
            FROM pg_stat_user_indexes
            WHERE schemaname IN ('production', 'quality', 'equipment', 'reporting', 'infrastructure', 'integration')
              AND idx_scan = 0
            ORDER BY pg_relation_size(indexrelid) DESC
            LIMIT 25
            """,
            null,
            cancellationToken);

        // pg_stat_statements 是可选扩展，未安装时不影响其余体检项
        List<Dictionary<string, object?>> slowStatements;
        try
        {
            slowStatements = await QueryAsync(
                """
                SELECT query, calls, round(total_exec_time::numeric, 2) AS total_ms,
                       round(mean_exec_time::numeric, 2) AS mean_ms, rows
                FROM pg_stat_statements
                ORDER BY total_exec_time DESC
                LIMIT 20
                """,
                null,
                cancellationToken);
        }
        catch (Exception exception)
        {
            slowStatements = [new Dictionary<string, object?> { ["note"] = $"pg_stat_statements 不可用：{exception.Message}" }];
        }

        return Ok(new { tables, unusedIndexes, slowStatements });
    }

    /// <summary>关键查询的执行计划（EXPLAIN ANALYZE，含实际耗时与缓冲区命中）。</summary>
    [HttpGet("explain")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> Explain(
        [FromQuery] string scenario = "sn-lookup",
        [FromQuery] string? arg = null,
        CancellationToken cancellationToken = default)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        if (!Scenarios.TryGetValue(scenario, out var definition))
        {
            return ApiResults.Problem(new QiaoMES.Shared.Error(
                "Performance.UnknownScenario", $"未知场景 {scenario}", QiaoMES.Shared.ErrorType.Validation));
        }

        var plan = await QueryAsync(
            $"EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) {definition.Sql}",
            arg ?? (string.IsNullOrEmpty(definition.DefaultArg) ? null : definition.DefaultArg),
            cancellationToken);

        return Ok(new { scenario, description = definition.Description, plan });
    }

    /// <summary>
    /// 并发基准测试：跑多次关键查询并统计 P50 / P95 / P99 / max（毫秒）。
    /// </summary>
    [HttpGet("benchmark")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> Benchmark(
        [FromQuery] string scenario = "sn-lookup",
        [FromQuery] int iterations = 200,
        [FromQuery] int concurrency = 8,
        [FromQuery] string? arg = null,
        CancellationToken cancellationToken = default)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        if (!Scenarios.TryGetValue(scenario, out var definition))
        {
            return ApiResults.Problem(new QiaoMES.Shared.Error(
                "Performance.UnknownScenario", $"未知场景 {scenario}", QiaoMES.Shared.ErrorType.Validation));
        }

        var total = Math.Clamp(iterations, 1, 5000);
        var parallel = Math.Clamp(concurrency, 1, 64);
        var parameter = arg ?? (string.IsNullOrEmpty(definition.DefaultArg) ? null : definition.DefaultArg);

        // 预热：避免首轮因连接/计划缓存未就绪拉高 P95
        for (var warmup = 0; warmup < Math.Min(10, total); warmup++)
        {
            await ExecuteScalarAsync(definition.Sql, parameter, cancellationToken);
        }

        var samples = new long[total];
        var started = Stopwatch.StartNew();

        await Parallel.ForAsync(0, total, new ParallelOptions
        {
            MaxDegreeOfParallelism = parallel,
            CancellationToken = cancellationToken,
        }, async (index, token) =>
        {
            var watch = Stopwatch.StartNew();
            await ExecuteScalarAsync(definition.Sql, parameter, token);
            watch.Stop();
            samples[index] = watch.ElapsedMilliseconds;
        });

        started.Stop();
        Array.Sort(samples);

        var wallClockSeconds = started.Elapsed.TotalSeconds;
        var report = new
        {
            scenario,
            description = definition.Description,
            iterations = total,
            concurrency = parallel,
            wallClockSeconds = Math.Round(wallClockSeconds, 2),
            throughputPerSecond = Math.Round(total / Math.Max(wallClockSeconds, 0.001), 1),
            p50Ms = Percentile(samples, 0.50),
            p95Ms = Percentile(samples, 0.95),
            p99Ms = Percentile(samples, 0.99),
            maxMs = samples[^1],
            minMs = samples[0],
            meanMs = Math.Round(samples.Average(), 2),
            sampleCount = total,
        };

        return Ok(report);
    }

    /// <summary>
    /// 造种子数据（压测用）。**只填必填列**，其余走默认值，能快速造出千万行级数据。
    /// </summary>
    [HttpPost("seed")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> Seed(
        [FromQuery] int snCount = 1_000_000,
        [FromQuery] int inspectionCount = 200_000,
        CancellationToken cancellationToken = default)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        // 取一个真实工单作为挂载点（SN 必须挂在工单上）
        var anchor = await QueryScalarAsync(
            """SELECT "Id"::text || ',' || "ProductId"::text FROM production.work_orders ORDER BY "CreatedAt" LIMIT 1""",
            null,
            cancellationToken);

        if (anchor is null)
        {
            return ApiResults.Problem(new QiaoMES.Shared.Error(
                "Performance.NoWorkOrder", "库里没有工单，请先创建至少一个工单再压测", QiaoMES.Shared.ErrorType.Validation));
        }

        var parts = anchor.ToString()!.Split(',');
        var workOrderId = parts[0];
        var productId = parts[1];

        var snInserted = snCount > 0
            ? await ExecuteNonQueryAsync(
                $"""
                 INSERT INTO production.serial_numbers
                     ("Id", "Sn", "WorkOrderId", "ProductId", "ProductCode", "Status", "CreatedAt", "IsDeleted")
                 SELECT gen_random_uuid(),
                        'SN-PERF-' || lpad(i::text, 9, '0'),
                        '{workOrderId}'::uuid,
                        '{productId}'::uuid,
                        'PERF-PRODUCT',
                        (i % 4),
                        now() - make_interval(secs => (i % 2592000)),
                        false
                 FROM generate_series(1, {snCount}) i
                 """,
                cancellationToken)
            : 0;

        var inspectionInserted = inspectionCount > 0
            ? await ExecuteNonQueryAsync(
                $"""
                 INSERT INTO quality.inspections
                     ("Id", "InspectionNumber", "Type", "Status", "Sn", "ProductCode", "SampleSize",
                      "AcceptedLimit", "RejectedLimit", "DefectQuantity", "Conclusion", "CreatedAt", "IsDeleted")
                 SELECT gen_random_uuid(),
                        'PERF-IQC-' || lpad(i::text, 9, '0'),
                        0,
                        (ARRAY[2,3,2,4])[(i % 4) + 1],
                        'SN-PERF-' || lpad((i % {Math.Max(snCount, 1)})::text, 9, '0'),
                        'PERF-PRODUCT',
                        5, 0, 1,
                        (i % 3),
                        (ARRAY[1,2,1,3])[(i % 4) + 1],
                        now() - make_interval(secs => (i % 2592000)),
                        false
                 FROM generate_series(1, {inspectionCount}) i
                 """,
                cancellationToken)
            : 0;

        // 更新统计信息，让优化器拿到真实行数（否则压测结果不可信）
        await ExecuteNonQueryAsync("ANALYZE production.serial_numbers", cancellationToken);
        await ExecuteNonQueryAsync("ANALYZE quality.inspections", cancellationToken);

        return Ok(new { snInserted, inspectionInserted, note = "已 ANALYZE，可开始 benchmark" });
    }

    /// <summary>清理压测种子数据（只删压测前缀，业务数据不受影响）。</summary>
    [HttpDelete("seed")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> CleanupSeed(CancellationToken cancellationToken)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        var snDeleted = await ExecuteNonQueryAsync(
            """DELETE FROM production.serial_numbers WHERE "Sn" LIKE 'SN-PERF-%'""", cancellationToken);

        var inspectionDeleted = await ExecuteNonQueryAsync(
            """DELETE FROM quality.inspections WHERE "InspectionNumber" LIKE 'PERF-IQC-%'""", cancellationToken);

        return Ok(new { snDeleted, inspectionDeleted });
    }

    // ---------------- SQL 执行 ----------------

    private async Task<List<Dictionary<string, object?>>> QueryAsync(
        string sql,
        string? parameter,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql, parameter);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(reader.FieldCount);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                row[reader.GetName(index)] = value is DBNull ? null : value is DateTime time ? time.ToString("O") : value;
            }
            rows.Add(row);
        }

        return rows;
    }

    private async Task<object?> QueryScalarAsync(string sql, string? parameter, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql, parameter);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is DBNull ? null : value;
    }

    private async Task<long> ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql, null);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ExecuteScalarAsync(string sql, string? parameter, CancellationToken cancellationToken)
    {
        // 每次执行独占一条连接，保证并发压测时互不干扰
        await using var independent = new NpgsqlConnection(ConnectionString);
        await independent.OpenAsync(cancellationToken);

        await using var command = independent.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;

        if (parameter is not null)
        {
            var parameterObject = command.CreateParameter();
            parameterObject.ParameterName = "arg";
            parameterObject.Value = parameter;
            command.Parameters.Add(parameterObject);
        }

        await command.ExecuteScalarAsync(cancellationToken);
    }

    private DbCommand CreateCommand(string sql, string? parameter)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        // 压测场景要显式超时，避免个别慢查询挂住整个测试
        command.CommandTimeout = 120;

        if (parameter is not null)
        {
            var parameterObject = command.CreateParameter();
            parameterObject.ParameterName = "arg";
            parameterObject.Value = parameter;
            command.Parameters.Add(parameterObject);
        }

        return command;
    }

    /// <summary>取分位值（samples 必须已升序）。</summary>
    private static long Percentile(long[] sortedSamples, double percentile)
    {
        if (sortedSamples.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * sortedSamples.Length) - 1;
        return sortedSamples[Math.Clamp(index, 0, sortedSamples.Length - 1)];
    }
}
