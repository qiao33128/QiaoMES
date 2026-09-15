using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace QiaoMES.Quality.Infrastructure.Persistence;

/// <summary>
/// 质量单号分配器：在数据库中原子自增（<c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c>），
/// 与业务写入处于同一事务，不会因并发重复。
/// </summary>
internal static class QualityNumberAllocator
{
    private const string Sql = """
        INSERT INTO quality.quality_number_sequences (sequence_key, last_value, updated_at)
        VALUES (@key, 1, now())
        ON CONFLICT (sequence_key)
        DO UPDATE SET last_value = quality.quality_number_sequences.last_value + 1,
                      updated_at = now()
        RETURNING last_value;
        """;

    public static async Task<int> NextAsync(QualityDbContext db, string key, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.CommandTimeout = 10;
            // 与业务写入共用同一事务，回滚时序号一起回滚
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            command.Parameters.Add(parameter);

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
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
