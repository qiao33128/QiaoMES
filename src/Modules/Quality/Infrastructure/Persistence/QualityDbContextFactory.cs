using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QiaoMES.Quality.Infrastructure.Persistence;

/// <summary>
/// 设计时上下文工厂：让 <c>dotnet ef migrations</c> 无需启动应用即可生成迁移。
/// </summary>
public sealed class QualityDbContextFactory : IDesignTimeDbContextFactory<QualityDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    public QualityDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QIAOMES_DEFAULT_DB")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new QualityDbContext(options);
    }
}
