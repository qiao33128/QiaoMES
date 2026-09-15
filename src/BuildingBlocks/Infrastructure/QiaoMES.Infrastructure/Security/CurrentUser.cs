using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using QiaoMES.Shared;

namespace QiaoMES.Infrastructure.Security;

/// <summary>基于 <see cref="HttpContext"/> 的当前用户实现。</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var raw = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? Principal?.FindFirst("sub")?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Username =>
        Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? Principal?.FindFirst("unique_name")?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}
