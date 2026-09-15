using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QiaoMES.Production.Domain;

namespace QiaoMES.Production.Infrastructure.Persistence;

/// <summary>
/// 基于数据库的工单号生成器。
/// <para>
/// 使用「按日计数表 + UPSERT ... RETURNING」在**单条 SQL 内**原子地取得下一个序号，
/// 天然并发安全，不依赖「先查 COUNT 再拼号」这种有竞态的做法。
/// </para>
/// </summary>
public sealed class WorkOrderNumberGenerator(ProductionDbContext db) : IWorkOrderNumberGenerator
{
    private const string NextValueSql = """
        INSERT INTO production.work_order_daily_sequences (sequence_date, last_value, updated_at)
        VALUES (@sequence_date, 1, now())
        ON CONFLICT (sequence_date)
        DO UPDATE SET last_value = production.work_order_daily_sequences.last_value + 1,
                      updated_at = now()
        RETURNING last_value;
        """;

    public async Task<string> NextAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var sequenceDate = DateOnly.FromDateTime(now);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            // 若当前上下文已登记事务，取号必须与业务写入处于同一事务，否则回滚后序号会「空洞」甚至重号
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = NextValueSql;
            command.CommandTimeout = 10;

            var parameter = command.CreateParameter();
            parameter.ParameterName = "sequence_date";
            parameter.Value = sequenceDate;
            command.Parameters.Add(parameter);

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            var nextValue = Convert.ToInt32(scalar);

            return $"WO-{sequenceDate:yyyyMMdd}-{nextValue:D4}";
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
