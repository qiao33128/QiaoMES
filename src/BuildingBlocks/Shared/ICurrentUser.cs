namespace QiaoMES.Shared;

/// <summary>
/// 当前登录用户上下文（由主机通过 HttpContext 实现）。
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Username { get; }
    bool IsAuthenticated { get; }
}
