using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Quality.Domain;
using QiaoMES.Quality.Infrastructure.Persistence;

namespace QiaoMES.Quality.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQualityInfrastructure(this IServiceCollection services)
    {
        // 复用主机注册的共享连接：与其它模块共用同一事务
        services.AddDbContext<QualityDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IInspectionRepository, InspectionRepository>();
        services.AddScoped<INonconformanceRepository, NonconformanceRepository>();
        services.AddScoped<IDefectCodeRepository, DefectCodeRepository>();
        services.AddScoped<IMaterialLotRepository, MaterialLotRepository>();

        return services;
    }
}
