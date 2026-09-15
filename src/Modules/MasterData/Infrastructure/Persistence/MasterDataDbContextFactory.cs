using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QiaoMES.MasterData.Infrastructure.Persistence;

/// <summary>
/// 设计时上下文工厂：让 <c>dotnet ef migrations</c> 无需启动整个应用（也不必连接数据库）。
/// </summary>
public sealed class MasterDataDbContextFactory : IDesignTimeDbContextFactory<MasterDataDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    public MasterDataDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QIAOMES_DEFAULT_DB")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MasterDataDbContext(options);
    }
}
