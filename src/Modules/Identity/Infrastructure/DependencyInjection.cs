using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Domain;
using QiaoMES.Identity.Infrastructure.Persistence;
using QiaoMES.Identity.Infrastructure.Security;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 复用主机注册的共享连接：一次请求内的多个 DbContext 才能共用同一事务
        services.AddDbContext<IdentityDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<DbConnection>()));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ITokenGenerator, JwtTokenGenerator>();

        // 权限来源：数据库（用户 → 角色 → 权限），带短缓存
        services.AddScoped<IPermissionProvider, DbPermissionProvider>();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();
        services.Configure<JwtOptions>(options =>
        {
            options.SecretKey = jwtOptions.SecretKey;
            options.Issuer = jwtOptions.Issuer;
            options.Audience = jwtOptions.Audience;
            options.ExpiresInHours = jwtOptions.ExpiresInHours;
        });

        return services;
    }
}
