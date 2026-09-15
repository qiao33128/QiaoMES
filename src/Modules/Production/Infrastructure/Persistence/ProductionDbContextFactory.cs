using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QiaoMES.Production.Infrastructure.Persistence;

/// <summary>
/// 设计时上下文工厂：让 <c>dotnet ef migrations</c> 无需启动整个应用（也不必连接数据库）。
/// 运行时仍然走主机注册的共享连接。
/// </summary>
public sealed class ProductionDbContextFactory : IDesignTimeDbContextFactory<ProductionDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    public ProductionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QIAOMES_DEFAULT_DB")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<ProductionDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ProductionDbContext(options);
    }
}
