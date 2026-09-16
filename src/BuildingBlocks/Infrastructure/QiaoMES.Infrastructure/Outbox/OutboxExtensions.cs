using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Infrastructure.Outbox;

public static class OutboxExtensions
{
    /// <summary>
    /// 注册 Outbox：存储（共享连接，纳入工作单元事务）、写入器、投递处理器与后台分发服务。
    /// </summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddDbContext<OutboxDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }

    /// <summary>
    /// 订阅一个集成事件：注册处理器并登记路由，Outbox 分发器据此在事件到达时调用它。<para>
    /// 同一事件可被多个模块分别订阅（每个订阅独立登记）。
    /// </para>
    /// </summary>
    public static IServiceCollection AddIntegrationEvent<TEvent, THandler>(this IServiceCollection services)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new OutboxSubscription(typeof(TEvent), typeof(THandler)));

        return services;
    }
}
