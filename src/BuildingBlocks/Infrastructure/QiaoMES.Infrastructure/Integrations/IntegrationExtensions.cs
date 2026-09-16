using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace QiaoMES.Infrastructure.Integrations;

public static class IntegrationExtensions
{
    /// <summary>开放 API 的限流策略名。</summary>
    public const string OpenApiRateLimitPolicy = "open-api";

    /// <summary>注册对外集成：API 客户端存储、管理服务与开放 API 限流策略。</summary>
    public static IServiceCollection AddIntegrations(this IServiceCollection services)
    {
        services.AddDbContext<IntegrationDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IApiClientRepository, ApiClientRepository>();
        services.AddScoped<IApiClientService, ApiClientService>();

        return services;
    }

}
