using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Domain;
using QiaoMES.Identity.Infrastructure.Persistence;
using QiaoMES.Identity.Infrastructure.Security;

namespace QiaoMES.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("IdentityDb")
            ?? throw new InvalidOperationException("未配置 IdentityDb 连接字符串");

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPasswordHasher, BcryptPasswordHasher>();
        services.AddScoped<ITokenGenerator, JwtTokenGenerator>();

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
