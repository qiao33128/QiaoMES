using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Domain;

namespace QiaoMES.Identity.Infrastructure.Persistence;

/// <summary>
/// 身份模块种子数据。
/// </summary>
public static class IdentityDbSeeder
{
    /// <summary>
    /// 初始化默认角色和管理员账号。
    /// </summary>
    public static async Task SeedAsync(IdentityDbContext db, IPasswordHasher passwordHasher, CancellationToken cancellationToken = default)
    {
        // 默认角色
        var roles = new[]
        {
            new Role("admin", "系统管理员"),
            new Role("supervisor", "班组长"),
            new Role("operator", "操作员"),
        };

        foreach (var role in roles)
        {
            var exists = await db.Roles.AnyAsync(r => r.Name == role.Name, cancellationToken);
            if (!exists)
            {
                db.Roles.Add(role);
            }
        }

        // 先保存角色，确保数据库中存在
        await db.SaveChangesAsync(cancellationToken);

        // 默认管理员账号：admin / Admin123!
        if (!await db.Users.AnyAsync(u => u.Username == "admin", cancellationToken))
        {
            var adminRole = await db.Roles.FirstAsync(r => r.Name == "admin", cancellationToken);
            var admin = new User("admin", passwordHasher.Hash("Admin123!"), "系统管理员");
            admin.AddRole(adminRole);
            db.Users.Add(admin);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
