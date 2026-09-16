using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Assistant.Application;

namespace QiaoMES.Assistant.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddAssistantModule(this IServiceCollection services)
    {
        services.AddScoped<IAssistantService, AssistantService>();
        return services;
    }

    public static IMvcBuilder AddAssistantControllers(this IMvcBuilder builder)
        => builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
