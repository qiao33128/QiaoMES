namespace QiaoMES.Shared.Authorization;

/// <summary>
/// 权限提供者：按用户解析其最终权限集合。
/// <para>
/// 由 Identity 模块实现（角色-权限关系存库），授权中间件只依赖本抽象，
/// 因此权限来源可从「数据库」平滑切换到「外部权限中心 + 缓存」。
/// </para>
/// </summary>
public interface IPermissionProvider
{
    /// <summary>解析用户的最终权限集合（角色权限的并集）。</summary>
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
