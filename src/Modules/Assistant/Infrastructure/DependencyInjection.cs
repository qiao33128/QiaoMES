using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Domain;
using QiaoMES.Assistant.Infrastructure.Llm;
using QiaoMES.Assistant.Infrastructure.Persistence;
using QiaoMES.Assistant.Infrastructure.Query;
using QiaoMES.Assistant.Infrastructure.Schema;

namespace QiaoMES.Assistant.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册智能问数基础设施:配置、语义层、只读执行器、OpenAI 兼容的模型客户端，以及配置持久化。
    /// <para>
    /// 注意两套数据库访问是**分开**的:
    /// <list type="bullet">
    /// <item><see cref="AssistantDbContext"/> 走主机的**共享连接**，只为「运行时配置」这一张单行表服务，
    /// 因此它参与业务请求的同一个事务是安全的；</item>
    /// <item>真正的问数查询走 <see cref="IReadOnlyQueryRunner"/> 自己开的**独立只读连接**，
    /// 不挂在业务事务上，也天然与写路径隔离。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static IServiceCollection AddAssistantInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(AssistantOptions.SectionName).Get<AssistantOptions>()
                      ?? new AssistantOptions();

        // 🔴 注册的是**实例**而不是工厂：所有消费方拿到同一个引用，
        // 因此「模型配置」页保存后改写它的属性就能立刻生效，不需要重启，也不需要 IOptionsMonitor。
        services.AddSingleton(options);

        services.AddSingleton<AssistantConnectionStrings>();
        services.AddSingleton<ISchemaProvider, DatabaseSchemaProvider>();
        services.AddSingleton<IReadOnlyQueryRunner, NpgsqlReadOnlyQueryRunner>();

        services.AddHttpClient<ISqlGenerator, OpenAiCompatibleSqlGenerator>(client =>
        {
            // 超时由生成器内部的 CancellationToken 统一控制，避免两层超时打架
            client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        });

        services.AddDbContext<AssistantDbContext>((serviceProvider, builder) =>
            builder.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IAssistantSettingsStore, AssistantSettingsStore>();

        return services;
    }
}
