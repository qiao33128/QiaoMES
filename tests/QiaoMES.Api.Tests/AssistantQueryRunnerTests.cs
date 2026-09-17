using Microsoft.Extensions.Configuration;
using QiaoMES.Assistant.Domain;
using QiaoMES.Assistant.Infrastructure;
using QiaoMES.Assistant.Infrastructure.Query;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 只读执行器的三层防线回归测试。
/// <para>
/// 背景：线上一条很普通的聚合查询稳定失败在
/// <c>A command is already in progress: SELECT * FROM (...) AS assistant_result LIMIT 201</c>，
/// 而且**第 1 轮就失败**（不是自我修复引起的）。
/// 根因是事务初始化那批 SET 语句被塞进**同一个命令**执行 —— Npgsql 对多语句命令只处理第一个结果集，
/// 连接就停在"命令进行中"状态，紧接着的主查询必然报错。
/// </para>
/// <para>
/// 所以这里断言的不只是"能查"，而是<b>同一条执行器连续执行都成功</b>（这正是当初逃过测试的盲区）。
/// </para>
/// </summary>
public class AssistantQueryRunnerTests
{
    [Fact]
    public async Task 连续执行多次查询_都要成功()
    {
        var runner = CreateRunner();

        for (var round = 1; round <= 3; round++)
        {
            var outcome = await runner.ExecuteAsync("SELECT 1 AS \"序号\", 'ok' AS \"结果\"", 10);

            Assert.True(outcome.IsSuccess, $"第 {round} 次执行失败：{outcome.Error}");
            Assert.Single(outcome.Result!.Rows);
            Assert.Equal(2, outcome.Result.Columns.Count);
            Assert.Equal(1, outcome.Result.Rows[0][0]);
        }
    }

    [Fact]
    public async Task 多语句查询也能执行_且不污染后续()
    {
        var runner = CreateRunner();

        // 真实场景里模型偶尔会写出带 CTE、甚至视觉上像多条语句的查询，这里确认 SET 批次之后依然干净
        var first = await runner.ExecuteAsync(
            "WITH lines AS (SELECT \"LineName\" FROM reporting.daily_shift_metrics LIMIT 5) SELECT count(*) AS \"条数\" FROM lines",
            10);
        Assert.True(first.IsSuccess, first.Error);

        var second = await runner.ExecuteAsync("SELECT count(*) AS \"表数\" FROM information_schema.tables", 10);
        Assert.True(second.IsSuccess, second.Error);
    }

    [Fact]
    public async Task 线上真实失败用例_按产线聚合的良率查询可以跑通()
    {
        // 这条 SQL 就是线上稳定报 `A command is already in progress` 的那一条。
        // SQL 本身完全合法（问题全在连接初始化），把它原样固化下来，防止再被同类改动弄坏。
        const string sql = """
            SELECT m."LineName" AS "产线",
                   round(100.0 * sum(m."CompletedSn") / nullif(sum(m."CompletedSn" + m."ScrappedSn"), 0), 2) AS "良率"
            FROM reporting.daily_shift_metrics m
            WHERE m."IsDeleted" = false
              AND m."ProductionDate" >= current_date - INTERVAL '7 days'
              AND m."LineName" IS NOT NULL
            GROUP BY m."LineName"
            ORDER BY "良率" DESC
            """;

        // 走与生产完全相同的两道处理：只读安全校验 → 强制外包 LIMIT
        var validation = SqlGuard.Validate(sql);
        Assert.True(validation.IsValid, validation.Reason);

        var runner = CreateRunner();
        var outcome = await runner.ExecuteAsync(SqlGuard.WrapWithLimit(validation.NormalizedSql!, 200), 200);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal(new[] { "产线", "良率" }, outcome.Result!.Columns.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task 只读事务挡住写操作()
    {
        var runner = CreateRunner();

        // 直接绕开 SqlGuard 调用执行器：验证"第二道防线"（数据库层面 READ ONLY）真的在拦
        var outcome = await runner.ExecuteAsync("CREATE TEMP TABLE qiaomes_readonly_probe (a int)", 10);

        Assert.False(outcome.IsSuccess);
        Assert.Contains("read-only", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 慢查询被statement_timeout掐断_且算SQL问题而不是环境问题()
    {
        var runner = CreateRunner(queryTimeoutSeconds: 1);

        var outcome = await runner.ExecuteAsync("SELECT pg_sleep(5)", 10);

        Assert.False(outcome.IsSuccess);

        // 断言 SQLSTATE 57014（statement_timeout 取消），而不是去匹配某句英文文案：
        // 文案会随 Npgsql / 服务端版本变，57014 不会。
        // 也正因为要拿到 57014，客户端超时必须留有余量 —— 客户端先超时只有 stream 错误，拿不到这个码。
        Assert.Contains("57014", outcome.Error!);

        // 这是 SQL 层面的问题（查询太重），必须**允许**自我修复，不能被判成环境问题直接放弃
        Assert.False(outcome.IsInfrastructure);
    }

    [Fact]
    public async Task 超过行数上限_标记为截断()
    {
        var runner = CreateRunner();

        var outcome = await runner.ExecuteAsync(
            "SELECT * FROM (SELECT generate_series(1, 50) AS \"序号\") AS assistant_result LIMIT 11",
            10);

        Assert.True(outcome.IsSuccess, outcome.Error);
        Assert.Equal(10, outcome.Result!.RowCount);
        Assert.True(outcome.Result.Truncated);
    }

    /// <summary>构造一个与线上配置同构的执行器（连接串取测试环境变量，默认指向本地库）。</summary>
    private static NpgsqlReadOnlyQueryRunner CreateRunner(int queryTimeoutSeconds = 15)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultDb")
            ?? "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

        var options = new AssistantOptions { QueryTimeoutSeconds = queryTimeoutSeconds };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultDb"] = connectionString,
            })
            .Build();

        return new NpgsqlReadOnlyQueryRunner(new AssistantConnectionStrings(options, configuration), options);
    }
}
