using QiaoMES.Assistant.Domain;

namespace QiaoMES.Domain.Tests;

/// <summary>
/// 只读 SQL 护栏测试。<para>
/// 这块逻辑是「让模型直接对数据库说话」的第一道防线,**必须**有测试:
/// 放过去一条 UPDATE,可能就是一次生产事故。
/// </para>
/// </summary>
public class SqlGuardTests
{
    [Fact]
    public void 合法的只读查询_应通过()
    {
        var sql = """
            SELECT w."WorkCenter" AS "产线", count(*) AS "工单数"
            FROM production.work_orders w
            WHERE w."IsDeleted" = false AND w."Status" = 3
            GROUP BY w."WorkCenter"
            """;

        var result = SqlGuard.Validate(sql);

        Assert.True(result.IsValid, result.Reason);
        Assert.Equal(sql, result.NormalizedSql);
    }

    [Fact]
    public void 带CTE的查询_应通过()
    {
        const string sql = """
            WITH recent AS (
                SELECT "Id", "ProductCode" FROM production.serial_numbers
                WHERE "IsDeleted" = false AND "CreatedAt" >= now() - interval '7 days'
            )
            SELECT "ProductCode", count(*) FROM recent GROUP BY "ProductCode"
            """;

        Assert.True(SqlGuard.Validate(sql).IsValid);
    }

    [Fact]
    public void 末尾分号_应被容忍并去掉()
    {
        var result = SqlGuard.Validate("SELECT 1;");

        Assert.True(result.IsValid);
        Assert.Equal("SELECT 1", result.NormalizedSql);
    }

    [Theory]
    [InlineData("UPDATE production.work_orders SET \"Status\" = 3")]
    [InlineData("DELETE FROM production.serial_numbers")]
    [InlineData("DROP TABLE production.work_orders")]
    [InlineData("INSERT INTO quality.defect_codes (\"Code\") VALUES ('X')")]
    [InlineData("TRUNCATE production.serial_numbers")]
    public void 写操作_一律拒绝(string sql)
    {
        var result = SqlGuard.Validate(sql);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void 多语句_应拒绝()
    {
        var result = SqlGuard.Validate("SELECT 1; SELECT 2");

        Assert.False(result.IsValid);
        Assert.Contains("分号", result.Reason);
    }

    [Fact]
    public void 注释_应拒绝()
    {
        Assert.False(SqlGuard.Validate("SELECT 1 -- 后面藏东西").IsValid);
        Assert.False(SqlGuard.Validate("SELECT /* 注释 */ 1").IsValid);
    }

    [Fact]
    public void 子查询里藏写操作_应拒绝()
    {
        const string sql = """
            SELECT * FROM (
                SELECT "Id" FROM production.serial_numbers
                WHERE "Sn" IN (SELECT "Sn" FROM updated_rows)
            ) t
            """;

        // 上面是合法的;真正要拦的是关键字出现在任何位置
        Assert.True(SqlGuard.Validate(sql).IsValid);
        Assert.False(SqlGuard.Validate(
            "SELECT * FROM (DELETE FROM production.serial_numbers RETURNING \"Id\") t").IsValid);
    }

    [Theory]
    [InlineData("SELECT * FROM identity.users")]
    [InlineData("SELECT * FROM information_schema.tables")]
    [InlineData("SELECT * FROM pg_catalog.pg_class")]
    public void 敏感schema_应拒绝(string sql)
    {
        var result = SqlGuard.Validate(sql);

        Assert.False(result.IsValid);
        Assert.Contains("schema", result.Reason);
    }

    [Theory]
    [InlineData("SELECT pg_sleep(600)")]
    [InlineData("SELECT pg_read_file('/etc/passwd')")]
    [InlineData("SELECT pg_terminate_backend(pid) FROM pg_stat_activity")]
    [InlineData("SELECT lo_export(1, '/tmp/x')")]
    public void 危险函数_应拒绝(string sql)
    {
        Assert.False(SqlGuard.Validate(sql).IsValid);
    }

    [Fact]
    public void 行级锁_应拒绝()
    {
        Assert.False(SqlGuard.Validate("SELECT * FROM production.work_orders FOR UPDATE").IsValid);
    }

    [Fact]
    public void 空语句_应拒绝()
    {
        Assert.False(SqlGuard.Validate(null).IsValid);
        Assert.False(SqlGuard.Validate("   ").IsValid);
        Assert.False(SqlGuard.Validate(";").IsValid);
    }

    /// <summary>
    /// 回归保护:列名里含 Update / Create / Delete 这些词根时不能被关键字扫描误伤,
    /// 否则「UpdatedAt / CreatedAt / IsDeleted」一出现就全盘拒绝,功能直接不可用。
    /// </summary>
    [Fact]
    public void 常见列名_不应被关键字误判()
    {
        const string sql = """
            SELECT w."OrderNumber", w."CreatedAt", w."CompletedAt", s."Status"
            FROM production.work_orders w
            JOIN production.serial_numbers s ON s."WorkOrderId" = w."Id"
            WHERE w."IsDeleted" = false AND s."IsDeleted" = false
            ORDER BY w."CreatedAt" DESC
            """;

        var result = SqlGuard.Validate(sql);

        Assert.True(result.IsValid, result.Reason);
    }

    [Fact]
    public void 外包LIMIT_应比上限多取一行用于判断截断()
    {
        var wrapped = SqlGuard.WrapWithLimit("SELECT \"Sn\" FROM production.serial_numbers", 200);

        Assert.EndsWith("LIMIT 201", wrapped);
        Assert.Contains("assistant_result", wrapped);
        Assert.Contains("SELECT \"Sn\" FROM production.serial_numbers", wrapped);
    }

    [Fact]
    public void 外包LIMIT_应去掉已有分号()
    {
        var wrapped = SqlGuard.WrapWithLimit("SELECT 1;", 10);

        Assert.DoesNotContain(";", wrapped);
        Assert.EndsWith("LIMIT 11", wrapped);
    }

    [Fact]
    public void 超长SQL_应拒绝()
    {
        var sql = "SELECT '" + new string('x', SqlGuard.MaxSqlLength + 10) + "'";

        Assert.False(SqlGuard.Validate(sql).IsValid);
    }
}
