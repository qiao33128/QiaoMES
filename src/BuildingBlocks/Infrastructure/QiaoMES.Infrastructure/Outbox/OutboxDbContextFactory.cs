using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QiaoMES.Infrastructure.Outbox;

/// <summary>设计时上下文工厂：让 <c>dotnet ef migrations</c> 无需启动应用。</summary>
public sealed class OutboxDbContextFactory : IDesignTimeDbContextFactory<OutboxDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    public OutboxDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QIAOMES_DEFAULT_DB")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new OutboxDbContext(options);
    }
}
