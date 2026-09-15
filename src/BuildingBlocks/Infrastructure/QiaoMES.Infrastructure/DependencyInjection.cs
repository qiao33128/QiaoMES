using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Exceptions;
using QiaoMES.Infrastructure.HealthChecks;
using QiaoMES.Infrastructure.Security;
using QiaoMES.Infrastructure.UnitOfWork;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// 注册横切基础设施：共享数据库连接、工作单元（事务）、统一异常处理、权限授权、当前用户、健康检查。
    /// </summary>
    public static IServiceCollection AddQiaoMESInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---------- 1. 共享数据库连接 ----------
        // 所有模块的 DbContext 复用同一个连接，才能把跨模块写入放进同一个事务。
        // 拆分微服务（各自独立数据库）时，本项会被替换为各模块独立连接 + Outbox/Saga。
        var connectionString = configuration.GetConnectionString("DefaultDb")
            ?? configuration.GetConnectionString("IdentityDb")
            ?? throw new InvalidOperationException("未配置数据库连接字符串（ConnectionStrings:DefaultDb）");

        services.AddScoped<DbConnection>(_ => new NpgsqlConnection(connectionString));

        // ---------- 2. 当前用户与提交后动作 ----------
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IPostCommitActions, PostCommitActions>();

        // ---------- 3. 统一异常处理 ----------
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // ---------- 4. 权限授权 ----------
        services.AddMemoryCache();
        services.AddSingleton<PermissionCacheEpoch>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        // ---------- 5. 健康检查 ----------
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }
}
