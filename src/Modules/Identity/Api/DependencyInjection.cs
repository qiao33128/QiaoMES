using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Identity.Application;

namespace QiaoMES.Identity.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }

    /// <summary>注册模块控制器程序集。</summary>
    public static IMvcBuilder AddIdentityControllers(this IMvcBuilder builder)
    {
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
