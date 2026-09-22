using QiaoMES.Shared;
using QiaoMES.Shared.Security;

namespace QiaoMES.Api.Iteration;

/// <summary>
/// 迭代服务配置的读写实现。
/// <para>
/// 🔴 **存储位置：服务器上的配置文件**（<c>config/iteration.json</c>，容器里是 <c>/app/config</c>，
/// 由编排挂载出来），**不进数据库** —— 管理员密钥与 <c>.env</c> 处在同一档保护（0600、只有属主可读）。
/// 见 <see cref="IterationSettingsFile"/> 里对"为什么不是 .env / 为什么不加密"的说明。
/// </para>
/// </summary>
public sealed class IterationSettingsStore(
    IterationSettingsFile file,
    ICurrentUser currentUser,
    ILogger<IterationSettingsStore> logger) : IIterationSettingsStore
{
    public Task<IterationSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Snapshot(file.Read()));

    public Task<Result<IterationSettingsSnapshot>> SaveAsync(
        IterationSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        var current = file.Read();

        // 未提供的项沿用"当前生效值"
        var baseUrl = update.ClearBaseUrl
            ? string.Empty                                     // 显式关停该功能
            : Normalize(update.BaseUrl) ?? current.BaseUrl;
        var timeout = update.TimeoutSeconds ?? current.TimeoutSeconds;

        if (Validate(baseUrl, timeout) is { } error)
        {
            return Task.FromResult(Result.Failure<IterationSettingsSnapshot>(error));
        }

        // 密钥：留空 = 沿用现有值（只改地址不会把密钥抹掉）；ClearAdminKey 才是显式清空
        var adminKey = update.ClearAdminKey
            ? string.Empty
            : string.IsNullOrWhiteSpace(update.AdminKey) ? current.AdminKey : update.AdminKey.Trim();

        var actor = currentUser.Username ?? "unknown";

        IterationEffectiveSettings effective;
        try
        {
            effective = file.Write(baseUrl, adminKey, timeout, actor);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 最常见的两种原因：config 目录没挂载（写进了容器可写层，重建就丢），或宿主机目录的属主不是容器用户
            logger.LogError(ex, "迭代服务：写配置文件失败（{Path}）", file.FilePath);
            return Task.FromResult(Result.Failure<IterationSettingsSnapshot>(Error.Validation(
                "Iteration.ConfigFileNotWritable",
                $"写配置文件失败：{ex.Message}。请检查 {file.FilePath} 所在目录是否已挂载、且容器用户对该目录有写权限。")));
        }

        return Task.FromResult(Result.Success(Snapshot(effective)));
    }

    private static IterationSettingsSnapshot Snapshot(IterationEffectiveSettings effective)
    {
        var hasKey = !string.IsNullOrWhiteSpace(effective.AdminKey);

        return new IterationSettingsSnapshot(
            effective.BaseUrl,
            effective.IsConfigured,
            hasKey,
            hasKey ? SecretProtector.Mask(effective.AdminKey) : null,
            effective.TimeoutSeconds,
            effective.Source,
            effective.UpdatedAt,
            effective.UpdatedBy);
    }

    /// <summary>空串一律当"未提供"处理（前端清空输入框时传的就是空串）。</summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error? Validate(string baseUrl, int timeout)
    {
        if (baseUrl.Length > 300)
        {
            return Error.Validation("Iteration.BaseUrlTooLong", "地址过长（最多 300 字符）");
        }

        if (!string.IsNullOrWhiteSpace(baseUrl)
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")))
        {
            return Error.Validation(
                "Iteration.InvalidBaseUrl",
                "迭代服务地址必须是完整 URL，例如 http://ai-iteration:8080（同容器网络用服务名；本机 Docker 用 http://host.docker.internal:8091）");
        }

        if (timeout is < 5 or > 900)
        {
            return Error.Validation("Iteration.InvalidTimeout", "超时必须在 5~900 秒之间");
        }

        return null;
    }
}
