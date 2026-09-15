using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Equipment.Domain;
using QiaoMES.Equipment.Infrastructure.Persistence;

namespace QiaoMES.Equipment.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddEquipmentInfrastructure(this IServiceCollection services)
    {
        // 复用主机注册的共享连接：与其它模块共用同一事务
        services.AddDbContext<EquipmentDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IEquipmentRepository, EquipmentRepository>();
        services.AddScoped<IAndonRepository, AndonRepository>();

        // Andon 超时自动升级（后台任务）
        services.AddHostedService<AndonEscalationService>();

        return services;
    }
}
