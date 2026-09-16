using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Domain;
using QiaoMES.Reporting.Infrastructure.Persistence;

namespace QiaoMES.Reporting.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<ReportingDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<ICalendarRepository, CalendarRepository>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<IMetricsAggregator, MetricsAggregator>();

        // 预聚合滚动重算（每 5 分钟刷新「昨天 + 今天」，覆盖跨天夜班）
        services.AddHostedService<MetricsAggregationWorker>();

        return services;
    }
}
