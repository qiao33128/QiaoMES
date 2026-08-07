namespace QiaoMES.Identity.Application.Contracts;

/// <summary>登录请求。</summary>
public record LoginRequest(string Username, string Password);

/// <summary>注册请求。</summary>
public record RegisterRequest(string Username, string Password, string DisplayName, string? Email = null);

/// <summary>令牌响应。</summary>
public record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAt,
    UserDto User);

/// <summary>用户信息。</summary>
public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string? Email,
    IReadOnlyList<string> Roles);
