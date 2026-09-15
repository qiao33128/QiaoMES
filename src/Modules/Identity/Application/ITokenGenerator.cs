using QiaoMES.Identity.Domain;

namespace QiaoMES.Identity.Application;

/// <summary>
/// JWT 令牌生成器。
/// </summary>
public interface ITokenGenerator
{
    /// <summary>
    /// 为用户生成访问令牌。
    /// </summary>
    /// <param name="user">用户。</param>
    /// <param name="roles">角色名集合。</param>
    /// <param name="permissions">权限标识集合（写入 claim 供前端渲染菜单；服务端鉴权不依赖它）。</param>
    string GenerateAccessToken(User user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions);
}
