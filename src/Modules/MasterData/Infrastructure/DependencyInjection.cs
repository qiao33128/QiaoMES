using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.MasterData.Domain;
using QiaoMES.MasterData.Infrastructure.Persistence;

namespace QiaoMES.MasterData.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMasterDataInfrastructure(this IServiceCollection services)
    {
        // 复用主机注册的共享连接：一次请求内的多个 DbContext 才能共用同一事务
        services.AddDbContext<MasterDataDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IBomRepository, BomRepository>();
        services.AddScoped<IRoutingRepository, RoutingRepository>();

        return services;
    }
}
