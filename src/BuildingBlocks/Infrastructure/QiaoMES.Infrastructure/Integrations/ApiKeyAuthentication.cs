using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QiaoMES.Infrastructure.Integrations;

public static class ApiKeyAuthenticationDefaults
{
    /// <summary>API Key 认证方案名（与 JWT 并存，开放 API 单独使用本方案）。</summary>
    public const string Scheme = "ApiKey";

    /// <summary>密钥请求头名称。</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>授权范围声明名。</summary>
    public const string ScopeClaim = "scope";
}

/// <summary>
/// 开放 API 的独立鉴权：<c>X-Api-Key</c> → SHA-256 摘要比对。<para>
/// 与 JWT 完全分离，便于给外部系统单独发放、单独吊销与单独限流；</para>
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IApiClientRepository repository)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationDefaults.HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var plainKey = headerValues.ToString();
        if (string.IsNullOrWhiteSpace(plainKey))
        {
            return AuthenticateResult.NoResult();
        }

        var client = await repository.GetByKeyHashAsync(ApiClient.ComputeHash(plainKey), Context.RequestAborted);
        if (client is null || !client.IsUsable)
        {
            Logger.LogWarning("开放 API 鉴权失败：密钥无效或已过期（前缀 {Preview}）", plainKey.Length > 12 ? plainKey[..12] : plainKey);
            return AuthenticateResult.Fail("API Key 无效、已停用或已过期");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, client.Id.ToString()),
            new(ClaimTypes.Name, client.Name),
            new(ApiKeyAuthenticationDefaults.ScopeClaim, client.Scopes),
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
