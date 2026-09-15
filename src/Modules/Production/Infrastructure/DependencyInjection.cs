using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Production.Domain;
using QiaoMES.Production.Infrastructure.Persistence;

namespace QiaoMES.Production.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductionInfrastructure(
        this IServiceCollection services)
    {
        // 复用主机注册的共享连接：一次请求内的多个 DbContext 才能共用同一事务
        services.AddDbContext<ProductionDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IWorkOrderRepository, WorkOrderRepository>();
        services.AddScoped<IWorkOrderNumberGenerator, WorkOrderNumberGenerator>();
        services.AddScoped<ISerialNumberRepository, SerialNumberRepository>();

        return services;
    }
}
