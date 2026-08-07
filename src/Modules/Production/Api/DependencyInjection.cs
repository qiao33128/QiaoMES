using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Production.Api.Hubs;
using QiaoMES.Production.Application;

namespace QiaoMES.Production.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddProductionModule(
        this IServiceCollection services)
    {
        services.AddScoped<IWorkOrderService, WorkOrderService>();
        services.AddScoped<IWorkOrderNotifier, WorkOrderNotifier>();
        services.AddSignalR();
        return services;
    }

    /// <summary>注册模块控制器程序集。</summary>
    public static IMvcBuilder AddProductionControllers(this IMvcBuilder builder)
    {
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
