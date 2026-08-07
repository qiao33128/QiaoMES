namespace QiaoMES.Identity.Infrastructure.Security;

/// <summary>
/// JWT 配置选项。
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "QiaoMES";
    public string Audience { get; set; } = "QiaoMES.Api";
    public int ExpiresInHours { get; set; } = 8;
}
