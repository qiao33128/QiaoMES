using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error.Code, message = result.Error.Description });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error.Code, message = result.Error.Description });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null) return Unauthorized();

        var result = await authService.GetCurrentUserAsync(userId.Value, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value)
            : BadRequest(new { error = result.Error.Code, message = result.Error.Description });
    }

    private Guid? GetUserId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirst("sub");
        return Guid.TryParse(sub?.Value, out var id) ? id : (Guid?)null;
    }
}
