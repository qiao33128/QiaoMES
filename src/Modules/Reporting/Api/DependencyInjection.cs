using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Reporting.Application;

namespace QiaoMES.Reporting.Api;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddScoped<IShiftService, ShiftService>();
        return services;
    }

    public static IMvcBuilder AddReportingControllers(this IMvcBuilder builder)
        => builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
