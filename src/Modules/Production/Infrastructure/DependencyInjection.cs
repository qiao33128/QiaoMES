using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Production.Domain;
using QiaoMES.Production.Infrastructure.Persistence;

namespace QiaoMES.Production.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ProductionDb")
            ?? throw new InvalidOperationException("未配置 ProductionDb 连接字符串");

        services.AddDbContext<ProductionDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();

        return services;
    }
}
