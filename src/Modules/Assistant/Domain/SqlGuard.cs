using System.Text.RegularExpressions;

namespace QiaoMES.Assistant.Domain;

/// <summary>
/// 只读 SQL 护栏(纯领域逻辑,可单元测试)。
/// <para>
/// 这是"让 AI 直接对数据库说话"的**第一道防线**,采用<b>白名单 + 黑名单 + 强制改写</b>三层策略:
/// </para>
/// <list type="number">
/// <item>白名单:整条语句必须是一个 <c>SELECT</c> / <c>WITH … SELECT</c>;</item>
/// <item>黑名单:拒绝写操作/DDL/会话控制关键字、注释、多语句、敏感 schema 与危险函数;</item>
/// <item>强制改写:无论模型写没写 <c>LIMIT</c>,一律外包一层带 <c>LIMIT</c> 的子查询,保证结果集有上界。</item>
/// </list>
/// <para>
/// 第二道防线在执行侧:数据库连接以 <c>SET TRANSACTION READ ONLY</c> 打开——即使护栏被绕过,
/// PostgreSQL 也会直接拒绝任何写入。两道防线都在,才敢把入口放开。
/// </para>
/// </summary>
public static partial class SqlGuard
{
    public const int DefaultMaxRows = 200;

    /// <summary>出现即拒绝的关键字(词边界匹配,避免误伤 UpdatedAt / IsDeleted 这类列名)。</summary>
    private static readonly string[] ForbiddenKeywords =
    [
        "insert", "update", "delete", "drop", "alter", "create", "truncate", "grant", "revoke",
        "merge", "copy", "call", "do", "vacuum", "analyze", "reindex", "cluster", "refresh",
        "set", "reset", "execute", "prepare", "deallocate", "declare", "move",
        "listen", "notify", "unlisten", "discard", "lock", "begin", "commit", "rollback",
        "savepoint", "release", "import", "security", "rename", "comment", "reassign", "deny",
        "abort", "checkpoint", "into", "returning",
    ];

    /// <summary>禁止访问的 schema(身份库与系统目录)。</summary>
    private static readonly string[] ForbiddenSchemas =
        ["identity", "information_schema", "pg_catalog", "pg_toast", "pg_temp"];

    /// <summary>禁止调用的函数(读写文件 / 睡死连接 / 终止会话 / 关闭日志)。</summary>
    private static readonly string[] ForbiddenFunctions =
    [
        "pg_sleep", "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_ls_waldir",
        "pg_terminate_backend", "pg_cancel_backend", "pg_reload_conf", "pg_rotate_logfile",
        "pg_stat_reset", "pg_stat_reset_shared", "lo_import", "lo_export", "dblink",
        "current_setting", "set_config", "pg_logical_emit_message",
    ];

    [GeneratedRegex(@"^\s*(select|with)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SelectPrefixRegex();

    [GeneratedRegex(@"\bfor\s+(update|share|no\s+key\s+update|key\s+share)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForUpdateRegex();

    /// <summary>长度上限:防止把整个 schema 塞进来或塞奇怪的载荷。</summary>
    public const int MaxSqlLength = 20_000;

    /// <summary>
    /// 校验只读 SQL。通过时返回去掉末尾分号后的语句(仍可能缺 <c>LIMIT</c>,由 <see cref="WrapWithLimit"/> 补)。
    /// </summary>
    public static SqlValidationResult Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlValidationResult.Invalid("SQL 为空");
        }

        var candidate = sql.Trim();
        while (candidate.EndsWith(';'))
        {
            candidate = candidate[..^1].TrimEnd();
        }

        if (candidate.Length == 0)
        {
            return SqlValidationResult.Invalid("SQL 为空");
        }

        if (candidate.Length > MaxSqlLength)
        {
            return SqlValidationResult.Invalid($"SQL 过长(超过 {MaxSqlLength} 字符)");
        }

        // 多语句:分号必须一个都不剩
        if (candidate.Contains(';', StringComparison.Ordinal))
        {
            return SqlValidationResult.Invalid("只允许单条语句,检测到分号分隔的多条语句");
        }

        // 注释:既是注入多语句的常见载体,也让关键字扫描失去意义
        if (candidate.Contains("--", StringComparison.Ordinal)
            || candidate.Contains("/*", StringComparison.Ordinal)
            || candidate.Contains("*/", StringComparison.Ordinal))
        {
            return SqlValidationResult.Invalid("不允许 SQL 注释");
        }

        if (!SelectPrefixRegex().IsMatch(candidate))
        {
            return SqlValidationResult.Invalid("只允许 SELECT / WITH 查询");
        }

        if (ForUpdateRegex().IsMatch(candidate))
        {
            return SqlValidationResult.Invalid("不允许行级锁(FOR UPDATE / FOR SHARE)");
        }

        foreach (var keyword in ForbiddenKeywords)
        {
            if (Regex.IsMatch(candidate, $@"\b{keyword}\b", RegexOptions.IgnoreCase))
            {
                return SqlValidationResult.Invalid($"不允许使用关键字 {keyword.ToUpperInvariant()}");
            }
        }

        foreach (var schema in ForbiddenSchemas)
        {
            if (Regex.IsMatch(candidate, $@"\b{schema}\s*\.", RegexOptions.IgnoreCase))
            {
                return SqlValidationResult.Invalid($"不允许访问 {schema} schema");
            }
        }

        foreach (var function in ForbiddenFunctions)
        {
            if (Regex.IsMatch(candidate, $@"\b{function}\s*\(", RegexOptions.IgnoreCase))
            {
                return SqlValidationResult.Invalid($"不允许调用函数 {function}");
            }
        }

        return SqlValidationResult.Ok(candidate);
    }

    /// <summary>
    /// 外包一层 <c>LIMIT</c> 子查询:模型漏写 LIMIT、或写了过大的 LIMIT 都能被兜住。
    /// 多取 1 行用于判断"是否被截断"。
    /// </summary>
    public static string WrapWithLimit(string sql, int maxRows)
    {
        var limit = maxRows <= 0 ? DefaultMaxRows : maxRows;
        var body = sql.Trim().TrimEnd(';');
        return $"SELECT * FROM (\n{body}\n) AS assistant_result LIMIT {limit + 1}";
    }
}
