using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Infrastructure.Authorization;

/// <summary>
/// 权限判定处理器。
/// <para>
/// 关键安全约定：**不信任 JWT 里的权限声明**，而是按用户 Id 从数据库（带短缓存）解析当前权限，
/// 这样「在线撤销权限」能立刻生效，而不必等令牌过期。
/// JWT 中的权限声明仅供前端展示菜单使用。
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler(
    IPermissionProvider permissionProvider,
    IMemoryCache cache,
    PermissionCacheEpoch epoch,
    ILogger<PermissionAuthorizationHandler> logger)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = ResolveUserId(context.User);
        if (userId is null)
        {
            logger.LogWarning("令牌中缺少可用的用户标识，无法进行权限判定");
            return;
        }

        var permissions = await cache.GetOrCreateAsync(
            PermissionCache.Key(epoch.Current, userId.Value),
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PermissionCache.Ttl;
                return await permissionProvider.GetPermissionsAsync(userId.Value);
            });

        if (permissions is not null && permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
            return;
        }

        logger.LogWarning("用户 {UserId} 缺少权限 {Permission}，请求被拒绝", userId.Value, requirement.Permission);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal principal)
    {
        // JwtBearer 默认会做 inbound claim 映射：sub → ClaimTypes.NameIdentifier
        var raw = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? principal.FindFirst("sub")?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
