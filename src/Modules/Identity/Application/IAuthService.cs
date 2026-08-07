using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.Identity.Application;

/// <summary>
/// 认证服务。
/// </summary>
public interface IAuthService
{
    Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<Result<TokenResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);
    Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
