using System.Data.Common;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Domain;

namespace QiaoMES.Assistant.Infrastructure.Schema;

/// <summary>
/// 语义层提供者。
/// <para>
/// 做法是「真实列结构 + 业务注解」两路合并:<b>列名与类型</b>从 <c>information_schema.columns</c> 实时读取
/// (所以永远不会讲错列名,表结构变了也不用改代码);<b>中文名 / 枚举取值 / 关键词</b>来自
/// <see cref="SemanticCatalog"/> 的人工注解(所以模型看得懂 <c>Status = 3</c> 是什么意思)。
/// </para>
/// <para>生成的文本会缓存(默认 10 分钟),避免每次提问都重复扫描系统目录、重复消耗 token。</para>
/// </summary>
public sealed class DatabaseSchemaProvider(
    AssistantConnectionStrings connectionStrings,
    AssistantOptions options,
    IMemoryCache cache) : ISchemaProvider
{
    private const string CacheKey = "assistant:schema-tables";

    public async Task<string> GetSchemaTextAsync(CancellationToken cancellationToken = default)
    {
        var tables = await LoadTablesAsync(cancellationToken);
        if (tables.Count == 0)
        {
            return "（语义层没有匹配到任何表：请确认数据库已完成迁移，且 schema 名与 SemanticCatalog 一致）";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"### 可用表（共 {tables.Count} 张，只能查询这些表）");
        builder.AppendLine();

        foreach (var table in tables)
        {
            builder.AppendLine($"#### {table.Schema}.{table.Table} — {table.BusinessName}");
            builder.AppendLine(table.Description);
            if (table.Keywords.Count > 0)
            {
                builder.AppendLine($"关键词：{string.Join(" / ", table.Keywords)}");
            }

            builder.AppendLine("列：");
            foreach (var column in table.Columns)
            {
                builder.Append($"  \"{column.Name}\" {column.DataType}");
                if (!string.Equals(column.BusinessName, column.Name, StringComparison.Ordinal))
                {
                    builder.Append($" — {column.BusinessName}");
                }

                if (!string.IsNullOrWhiteSpace(column.Description))
                {
                    builder.Append($"（{column.Description}）");
                }

                if (column.EnumValues is { Count: > 0 })
                {
                    var enums = string.Join(", ", column.EnumValues.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));
                    builder.Append($"［{enums}］");
                }

                builder.AppendLine();
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public Task<string> GetGlossaryTextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(SemanticCatalog.Glossary);

    public async Task<int> GetTableCountAsync(CancellationToken cancellationToken = default)
        => (await LoadTablesAsync(cancellationToken)).Count;

    private async Task<IReadOnlyList<TableSchema>> LoadTablesAsync(CancellationToken cancellationToken)
        => await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(Math.Max(1, options.SchemaCacheMinutes));
            return await BuildAsync(cancellationToken);
        }) ?? [];

    private async Task<IReadOnlyList<TableSchema>> BuildAsync(CancellationToken cancellationToken)
    {
        var schemas = SemanticCatalog.Tables.Values
            .Select(t => t.Schema)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var actual = new Dictionary<string, List<(string Name, string DataType, int Ordinal)>>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new NpgsqlConnection(connectionStrings.Value);
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT table_schema, table_name, column_name, data_type, ordinal_position
                FROM information_schema.columns
                WHERE table_schema = ANY(@schemas)
                ORDER BY table_schema, table_name, ordinal_position
                """;
            command.Parameters.Add(new NpgsqlParameter("schemas", schemas));

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                var column = (reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
                if (!actual.TryGetValue(key, out var columns))
                {
                    columns = [];
                    actual[key] = columns;
                }

                columns.Add(column);
            }
        }

        var result = new List<TableSchema>();
        foreach (var semantic in SemanticCatalog.Tables.Values)
        {
            if (!actual.TryGetValue(semantic.Key, out var columns) || columns.Count == 0)
            {
                continue;
            }

            var merged = columns
                // EF 生成的影子外键列（WorkOrderId1 / RoutingId1）对问数毫无意义，直接隐藏
                .Where(c => !IsShadowColumn(c.Name))
                // IsDeleted 不喂给模型：语义层已用规则统一要求过滤，列出来反而诱导模型 SELECT 它
                .Where(c => !string.Equals(c.Name, "IsDeleted", StringComparison.Ordinal))
                // 保持数据库里的列定义顺序（与实体定义一致），比按注解顺序更符合直觉
                .Select(c =>
                {
                    var annotation = semantic.Columns.TryGetValue(c.Name, out var value) ? value : null;
                    return new ColumnSchema(
                        c.Name,
                        c.DataType,
                        annotation?.BusinessName ?? c.Name,
                        annotation?.Description,
                        annotation?.EnumValues);
                })
                .ToList();

            result.Add(new TableSchema(
                semantic.Schema,
                semantic.Table,
                semantic.BusinessName,
                semantic.Description,
                merged,
                semantic.Keywords));
        }

        return result;
    }

    private static bool IsShadowColumn(string name)
        => name.EndsWith("Id1", StringComparison.Ordinal);
}
