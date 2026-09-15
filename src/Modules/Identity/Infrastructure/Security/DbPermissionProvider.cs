using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Infrastructure.Persistence;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Infrastructure.Security;

/// <summary>
/// 从数据库解析用户权限：用户 → 角色 → 权限（并集）。
/// <para>
/// 授权判定基于这里的结果而不是 JWT 中的声明，因此「撤销权限」「停用账号」可立即生效，
/// 无需等待令牌过期。
/// </para>
/// </summary>
public sealed class DbPermissionProvider(IdentityDbContext db) : IPermissionProvider
{
    private static readonly IReadOnlySet<string> EmptyPermissions = new HashSet<string>(StringComparer.Ordinal);

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // 账号被停用 → 立即失去全部业务权限
        var isActive = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (bool?)u.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (isActive != true)
        {
            return EmptyPermissions;
        }

        // 与 Roles 关联查询：角色被软删除后，其权限不再生效
        var roleIds = await (from userRole in db.UserRoles.AsNoTracking()
                             join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                             where userRole.UserId == userId && !userRole.IsDeleted
                             select userRole.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return EmptyPermissions;
        }

        var permissions = await db.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId) && !rp.IsDeleted)
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);

        return permissions.ToHashSet(StringComparer.Ordinal);
    }
}
