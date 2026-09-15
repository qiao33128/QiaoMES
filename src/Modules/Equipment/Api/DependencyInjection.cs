using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Equipment.Api.Hubs;
using QiaoMES.Equipment.Application;

namespace QiaoMES.Equipment.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddEquipmentModule(this IServiceCollection services)
    {
        services.AddScoped<IEquipmentService, EquipmentService>();
        services.AddScoped<IAndonService, AndonService>();
        services.AddScoped<IAndonNotifier, AndonNotifier>();
        return services;
    }

    /// <summary>注册模块控制器程序集。</summary>
    public static IMvcBuilder AddEquipmentControllers(this IMvcBuilder builder)
        => builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
