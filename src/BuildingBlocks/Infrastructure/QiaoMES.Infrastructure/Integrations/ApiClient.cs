using System.Security.Cryptography;
using System.Text;
using QiaoMES.Shared;

namespace QiaoMES.Infrastructure.Integrations;

/// <summary>
/// 开放 API 客户端（ERP / 设备网关等外部系统）。<para>
/// 只保存密钥的 SHA-256 摘要与可识别前缀，明文密钥仅在创建时返回一次。
/// </para>
/// </summary>
public class ApiClient : Entity
{
    /// <summary>密钥前缀（便于运维识别，不含敏感信息）。</summary>
    public const string KeyPrefix = "qmk_";

    private ApiClient() { }

    public ApiClient(string name, string apiKeyHash, string keyPreview, string? scopes = null, DateTime? expiresAt = null, string? remark = null)
        : base(Guid.NewGuid())
    {
        Name = name.Trim();
        ApiKeyHash = apiKeyHash;
        KeyPreview = keyPreview;
        Scopes = string.IsNullOrWhiteSpace(scopes) ? "open:read,open:write" : scopes.Trim();
        ExpiresAt = expiresAt;
        Remark = remark?.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>密钥摘要（SHA-256 十六进制小写）。</summary>
    public string ApiKeyHash { get; private set; } = string.Empty;

    /// <summary>密钥可识别前缀，如 <c>qmk_3f9a…</c>。</summary>
    public string KeyPreview { get; private set; } = string.Empty;

    /// <summary>授权范围（逗号分隔），如 <c>open:read,open:write</c>。</summary>
    public string Scopes { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public DateTime? LastUsedAt { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    /// <summary>是否仍然有效（启用且未过期）。</summary>
    public bool IsUsable => IsActive && (ExpiresAt is null || ExpiresAt > DateTime.UtcNow);

    /// <summary>生成新密钥并返回明文（调用方负责一次性展示）。</summary>
    public static (string PlainKey, string Hash, string Preview) GenerateKey()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
        var plain = KeyPrefix + raw;

        return (plain, ComputeHash(plain), plain[..12] + "…");
    }

    /// <summary>计算密钥摘要。</summary>
    public static string ComputeHash(string plainKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainKey))).ToLowerInvariant();

    public void MarkUsed() => LastUsedAt = DateTime.UtcNow;

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string name, string scopes, DateTime? expiresAt, string? remark)
    {
        Name = name.Trim();
        Scopes = scopes.Trim();
        ExpiresAt = expiresAt;
        Remark = remark?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
