using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Quality.Application;

namespace QiaoMES.Quality.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddQualityModule(this IServiceCollection services)
    {
        services.AddScoped<IInspectionService, InspectionService>();
        services.AddScoped<INonconformanceService, NonconformanceService>();
        services.AddScoped<IDefectCodeService, DefectCodeService>();
        services.AddScoped<ISpcService, SpcService>();
        return services;
    }

    /// <summary>注册模块控制器程序集。</summary>
    public static IMvcBuilder AddQualityControllers(this IMvcBuilder builder)
        => builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
