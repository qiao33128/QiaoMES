using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QiaoMES.Assistant.Infrastructure.Persistence;

/// <summary>
/// 设计时工程（<c>dotnet ef migrations</c> 用）。与其它模块同构：先读 <c>QIAOMES_DEFAULT_DB</c>，
/// 没有再回退到本地默认连接串。
/// </summary>
public sealed class AssistantDbContextFactory : IDesignTimeDbContextFactory<AssistantDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    public AssistantDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QIAOMES_DEFAULT_DB")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<AssistantDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AssistantDbContext(options);
    }
}
