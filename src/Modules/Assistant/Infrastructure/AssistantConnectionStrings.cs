using Microsoft.Extensions.Configuration;
using QiaoMES.Assistant.Domain;

namespace QiaoMES.Assistant.Infrastructure;

/// <summary>
/// 解析"问数用"的连接串。<para>
/// 生产环境务必用 <c>Assistant:ConnectionString</c> 指向一个<b>只读账号</b>(建库脚本见 docs/AI-QUERY.md);
/// 未配置时退化为复用 <c>ConnectionStrings:DefaultDb</c>,此时仍需依赖 <c>SET TRANSACTION READ ONLY</c> 兜底。
/// </para>
/// </summary>
public sealed class AssistantConnectionStrings(AssistantOptions options, IConfiguration configuration)
{
    private readonly string _connectionString = Resolve(options, configuration);

    /// <summary>是否配置了独立的(只读)连接串。</summary>
    public bool IsDedicated => !string.IsNullOrWhiteSpace(options.ConnectionString);

    public string Value => _connectionString;

    private static string Resolve(AssistantOptions options, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return options.ConnectionString!;
        }

        return configuration.GetConnectionString("DefaultDb")
            ?? throw new InvalidOperationException(
                "智能问数缺少数据库连接串:请配置 Assistant:ConnectionString 或 ConnectionStrings:DefaultDb");
    }
}
