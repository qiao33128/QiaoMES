using System.Diagnostics;
using System.Globalization;
using Npgsql;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Application.Contracts;
using QiaoMES.Assistant.Domain;

namespace QiaoMES.Assistant.Infrastructure.Query;

/// <summary>
/// 只读执行器 —— 智能问数的**第二道防线**。
/// <para>
/// 每次执行都新开一条独立连接,并在事务里先执行:
/// <c>SET TRANSACTION READ ONLY</c>(数据库层面拒绝任何写操作)+
/// <c>SET LOCAL statement_timeout</c>(杜绝慢查询拖死连接)。
/// 也就是说:即便 <see cref="SqlGuard"/> 被绕过,PostgreSQL 自己也会拦下来。
/// </para>
/// </summary>
public sealed class NpgsqlReadOnlyQueryRunner(
    AssistantConnectionStrings connectionStrings,
    AssistantOptions options) : IReadOnlyQueryRunner
{
    public bool UsesDedicatedConnection => connectionStrings.IsDedicated;

    public async Task<QueryExecutionOutcome> ExecuteAsync(
        string sql,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var timeoutSeconds = Math.Clamp(options.QueryTimeoutSeconds, 1, 300);
        var limit = maxRows <= 0 ? SqlGuard.DefaultMaxRows : maxRows;
        var watch = Stopwatch.StartNew();

        try
        {
            await using var connection = new NpgsqlConnection(connectionStrings.Value);
            await connection.OpenAsync(cancellationToken);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var setup = connection.CreateCommand())
            {
                setup.Transaction = transaction;
                setup.CommandText =
                    $"""
                     SET TRANSACTION READ ONLY;
                     SET LOCAL statement_timeout = {timeoutSeconds * 1000};
                     SET LOCAL idle_in_transaction_session_timeout = {timeoutSeconds * 1000};
                     """;
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.CommandTimeout = timeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<QueryColumnDto>(reader.FieldCount);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                columns.Add(new QueryColumnDto(reader.GetName(index), reader.GetDataTypeName(index)));
            }

            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken))
            {
                // SQL 已被外包一层 LIMIT (maxRows + 1):能读到第 maxRows+1 行就说明被截断了
                if (rows.Count >= limit)
                {
                    truncated = true;
                    break;
                }

                var values = new object?[reader.FieldCount];
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    values[index] = Normalize(reader.GetValue(index));
                }

                rows.Add(values);
            }

            // 只读事务没有任何写入,直接回滚即可(避免留下长事务)
            await transaction.RollbackAsync(cancellationToken);
            watch.Stop();

            return QueryExecutionOutcome.Ok(new QueryResultDto(
                columns, rows, rows.Count, truncated, watch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            watch.Stop();
            return QueryExecutionOutcome.Failed(Simplify(exception.Message));
        }
    }

    private static object? Normalize(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        _ => value,
    };

    /// <summary>把 Npgsql 的报错压成一行,方便直接回灌给模型做自我修复。</summary>
    private static string Simplify(string message)
    {
        var single = string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return single.Length <= 800 ? single : single[..800] + "…";
    }
}
