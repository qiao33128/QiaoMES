using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Domain;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Infrastructure.Persistence;

/// <summary>
/// 身份模块种子数据：默认角色（含权限）与管理员账号。
/// </summary>
public static class IdentityDbSeeder
{
    /// <summary>
    /// 角色 → 默认权限。这是「开箱可用」的初始配置，之后可在角色管理页面里调整。
    /// </summary>
    private static readonly Dictionary<string, (string Description, string[] Permissions)> DefaultRoles = new()
    {
        ["admin"] = ("系统管理员", Permissions.All.ToArray()),
        ["supervisor"] =
        (
            "班组长",
            [
                Permissions.WorkOrders.Read,
                Permissions.WorkOrders.Create,
                Permissions.WorkOrders.Update,
                Permissions.WorkOrders.Release,
                Permissions.WorkOrders.Start,
                Permissions.WorkOrders.Report,
                Permissions.WorkOrders.Complete,
                Permissions.WorkOrders.Cancel,
                Permissions.MasterData.Read,
                Permissions.MasterData.Manage,
                Permissions.Quality.Read,
                Permissions.Quality.Inspect,
                Permissions.Quality.Manage,
                Permissions.Equipment.Read,
                Permissions.Equipment.Operate,
                Permissions.Equipment.Manage,
                Permissions.Reporting.Read,
                Permissions.Reporting.Manage,
                Permissions.Integration.Read,
                Permissions.Integration.Manage,
                Permissions.Users.Read,
                Permissions.Roles.Read,
                Permissions.Assistant.Read,
                Permissions.Assistant.Ask,
                // 主管可以对自己可用的功能提改进建议；审阅计划仍限管理员（iteration:manage 不在内置角色里）
                Permissions.Iteration.Suggest,
            ]
        ),
        ["operator"] =
        (
            "操作员",
            [
                Permissions.WorkOrders.Read,
                Permissions.WorkOrders.Start,
                Permissions.WorkOrders.Report,
                Permissions.MasterData.Read,
                Permissions.Quality.Read,
                Permissions.Quality.Inspect,
                Permissions.Equipment.Read,
                Permissions.Equipment.Operate,
                Permissions.Reporting.Read,
            ]
        ),
    };

    public static async Task SeedAsync(
        IdentityDbContext db,
        IPasswordHasher passwordHasher,
        CancellationToken cancellationToken = default)
    {
        // ---------- 角色与权限 ----------
        foreach (var (roleName, definition) in DefaultRoles)
        {
            var role = await db.Roles
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);

            if (role is null)
            {
                role = new Role(roleName, definition.Description);
                foreach (var permission in definition.Permissions)
                {
                    role.GrantPermission(permission);
                }

                // 新角色：Add 会把整个图（含权限关联）级联标记为新增
                db.Roles.Add(role);
                continue;
            }

            // 已存在：补齐内置权限（不回收人为调整过的权限）。
            // 新建的关联必须显式 Add，否则 EF 会按 UPDATE 处理并抛并发异常。
            foreach (var permission in definition.Permissions)
            {
                var created = role.GrantPermission(permission);
                if (created is not null)
                {
                    db.RolePermissions.Add(created);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // ---------- 默认管理员 ----------
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
