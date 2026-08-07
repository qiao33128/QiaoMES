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
    string GenerateAccessToken(User user, IReadOnlyList<string> roles);
}
