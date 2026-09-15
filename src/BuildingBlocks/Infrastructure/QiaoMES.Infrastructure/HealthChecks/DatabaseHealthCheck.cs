using System.Data.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QiaoMES.Infrastructure.HealthChecks;

/// <summary>
/// 数据库就绪探针：使用共享连接执行 <c>SELECT 1</c>，确认数据库可达且凭据有效。
/// </summary>
public sealed class DatabaseHealthCheck(DbConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 5;
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("数据库连接正常");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("数据库不可用", exception);
        }
    }
}
