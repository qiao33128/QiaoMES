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

            // 🔴 这三条 SET 必须**逐条**执行,不能拼成一个多语句命令。
            // Npgsql 对多语句命令只处理第一个结果集,连接会停在"命令进行中"状态,
            // 紧接着的主查询必定抛 `A command is already in progress`。
            // （线上第一条真实的问数就是这么炸的,而且它看起来像"模型写错了 SQL",极难猜。）
            var setupTimeout = timeoutSeconds * 1000;
            async Task SetAsync(string statement)
            {
                await using var setup = connection.CreateCommand();
                setup.Transaction = transaction;
                setup.CommandText = statement;
                await setup.ExecuteNonQueryAsync(cancellationToken);
            }

            await SetAsync("SET TRANSACTION READ ONLY");
            await SetAsync($"SET LOCAL statement_timeout = {setupTimeout}");
            await SetAsync($"SET LOCAL idle_in_transaction_session_timeout = {setupTimeout}");

            var columns = new List<QueryColumnDto>();
            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;

            // reader 必须在这个作用域内读完并释放,之后才允许 Rollback ——
            // 否则连接还停在 Fetching,回滚会撞上同一个 "A command is already in progress"
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                // 客户端超时给 5 秒余量,让**服务端**的 statement_timeout 先触发:
                // 服务端取消返回的是干净的 57014「canceling statement due to statement timeout」——
                // 那是 SQL 层面的错误,模型看得懂(可以加过滤条件重写);
                // 而客户端先超时只会得到一句 network/stream 错误,会被判成"环境问题"直接放弃修复。
                command.CommandTimeout = timeoutSeconds + 5;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);

                for (var index = 0; index < reader.FieldCount; index++)
                {
                    columns.Add(new QueryColumnDto(reader.GetName(index), reader.GetDataTypeName(index)));
                }

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
            }

            // 只读事务没有任何写入,直接回滚即可(避免留下长事务)。
            // 这里容忍失败:只读事务没有需要撤销的更改,回滚出了问题也不该把已经拿到手的结果丢掉。
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception)
            {
                // 连接可能已被服务端回收,下次执行会开新连接
            }

            watch.Stop();

            return QueryExecutionOutcome.Ok(new QueryResultDto(
                columns, rows, rows.Count, truncated, watch.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PostgresException exception)
        {
            // SQL 本身的问题(列名写错 / 表不存在 / 类型不匹配 / 只读事务拒写)。
            // 带上 SQLSTATE:模型看到 42P01、42703、25006 这类码,比看一段自然语言更容易改对。
            watch.Stop();
            return QueryExecutionOutcome.Failed($"[{exception.SqlState}] {Simplify(exception.MessageText)}");
        }
        catch (NpgsqlException exception)
        {
            // 连不上 / 连接断了 / 协议错乱 / 超时 —— 执行环境的问题,与 SQL 无关。
            // 标记为 infrastructure,让上层别再浪费模型调用去"修复"。
            watch.Stop();
            return QueryExecutionOutcome.Failed(
                $"执行环境异常(与 SQL 无关,请检查数据库连接或稍后重试):{Simplify(exception.Message)}",
                isInfrastructure: true);
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
