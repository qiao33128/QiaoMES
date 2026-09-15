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
        return services;
    }

    /// <summary>注册模块控制器程序集。</summary>
    public static IMvcBuilder AddMasterDataControllers(this IMvcBuilder builder)
    {
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
