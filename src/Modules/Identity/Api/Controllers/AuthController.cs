using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared;

namespace QiaoMES.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await authService.LoginAsync(request, cancellationToken));

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await authService.RegisterAsync(request, cancellationToken));

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return ApiResults.Problem(Error.Unauthorized("Auth.Unauthenticated", "未登录或令牌无效"));
        }

        return ApiResults.FromResult(await authService.GetCurrentUserAsync(userId.Value, cancellationToken));
    }
}
