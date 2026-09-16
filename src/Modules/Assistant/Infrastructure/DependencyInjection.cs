using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Domain;
using QiaoMES.Assistant.Infrastructure.Llm;
using QiaoMES.Assistant.Infrastructure.Query;
using QiaoMES.Assistant.Infrastructure.Schema;

namespace QiaoMES.Assistant.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册智能问数基础设施:配置、语义层、只读执行器、OpenAI 兼容的模型客户端。
    /// <para>
    /// 注意:这里**不注册 DbContext**——问数走自己的只读连接,不参与业务请求的事务,
    /// 这样既不会把慢查询挂在业务事务上,也天然与写路径隔离。
    /// </para>
    /// </summary>
    public static IServiceCollection AddAssistantInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(AssistantOptions.SectionName).Get<AssistantOptions>()
                      ?? new AssistantOptions();

        services.AddSingleton(options);
        services.AddSingleton<AssistantConnectionStrings>();
        services.AddSingleton<ISchemaProvider, DatabaseSchemaProvider>();
        services.AddSingleton<IReadOnlyQueryRunner, NpgsqlReadOnlyQueryRunner>();

        services.AddHttpClient<ISqlGenerator, OpenAiCompatibleSqlGenerator>(client =>
        {
            // 超时由生成器内部的 CancellationToken 统一控制，避免两层超时打架
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        return services;
    }
}
