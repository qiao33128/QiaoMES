using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QiaoMES.Equipment.Infrastructure.Persistence;

/// <summary>设计时上下文工厂：让 <c>dotnet ef migrations</c> 无需启动应用。</summary>
public sealed class EquipmentDbContextFactory : IDesignTimeDbContextFactory<EquipmentDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    public EquipmentDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QIAOMES_DEFAULT_DB")
            ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<EquipmentDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new EquipmentDbContext(options);
    }
}
