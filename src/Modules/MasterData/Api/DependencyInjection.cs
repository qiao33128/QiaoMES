using Microsoft.Extensions.DependencyInjection;
using QiaoMES.MasterData.Application;

namespace QiaoMES.MasterData.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddMasterDataModule(this IServiceCollection services)
    {
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IMaterialService, MaterialService>();
        services.AddScoped<IWorkCenterService, WorkCenterService>();
        services.AddScoped<IOperationService, OperationService>();
        services.AddScoped<IBomService, BomService>();
        services.AddScoped<IRoutingService, RoutingService>();

        // 供其它模块（Production）读取主数据的只读契约
        services.AddScoped<IMasterDataQueryService, MasterDataQueryService>();
        return services;
    }

    /// <summary>注册模块控制器程序集。</summary>
    public static IMvcBuilder AddMasterDataControllers(this IMvcBuilder builder)
    {
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
