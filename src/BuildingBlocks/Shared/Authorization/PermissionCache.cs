namespace QiaoMES.Shared.Authorization;

/// <summary>
/// 权限缓存的 Key 与有效期约定。
/// <para>
/// 放在 Shared 是为了让「写入方」（Identity 模块，负责变更角色权限）与「读取方」（授权处理器）
/// 使用同一套 Key，从而在权限变更时能精确失效缓存。
/// </para>
/// <para>
/// Key 中带一个全局「代次(epoch)」：角色权限一旦变更就把代次 +1，
/// 所有旧缓存条目自然失效（并在 TTL 到期后被回收），无需逐个用户遍历清理。
/// </para>
/// </summary>
public static class PermissionCache
{
    /// <summary>单个用户的权限集合缓存时长。越短越实时、越长越省库查询。</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    /// <summary>构造某代次下某用户的权限缓存 Key。</summary>
    public static string Key(long epoch, Guid userId) => $"qiaomes:permissions:{epoch}:{userId:N}";

    /// <summary>构造用户自身的缓存 Key（与代次无关），用于「用户被停用/改角色」时精确失效。</summary>
    public static string UserKey(Guid userId) => $"qiaomes:permissions:user:{userId:N}";
}

/// <summary>
/// 权限缓存代次。角色-权限关系变更时递增，使全部用户的权限缓存立即失效。
/// </summary>
public sealed class PermissionCacheEpoch
{
    private long _epoch;

    public long Current => Interlocked.Read(ref _epoch);

    /// <summary>使当前所有权限缓存失效。</summary>
    public void Bump() => Interlocked.Increment(ref _epoch);
}
