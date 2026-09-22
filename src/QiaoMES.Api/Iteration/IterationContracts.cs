using QiaoMES.Shared;

namespace QiaoMES.Api.Iteration;

/// <summary>
/// 迭代服务的**生效配置**快照（不是文件原样，而是"当前真正在用的值"）。
/// <para>
/// 刻意不返回管理员密钥明文：只给 <see cref="HasAdminKey"/> 与掩码，
/// 页面据此显示「已配置 aiit****2026」。明文既不进接口、也不进数据库。
/// </para>
/// </summary>
public sealed record IterationSettingsSnapshot(
    string BaseUrl,
    /// <summary>地址是否已填（false 时「改进建议」页会提示"还没配置"）。</summary>
    bool Configured,
    bool HasAdminKey,
    string? AdminKeyMasked,
    int TimeoutSeconds,
    /// <summary><c>file</c> = 有人在页面上配过（存在服务器的配置文件里）；<c>configuration</c> = 仍在用 appsettings / 环境变量。</summary>
    string Source,
    DateTime? UpdatedAt,
    string? UpdatedBy);

/// <summary>
/// 配置变更请求。<para>
/// 所有字段都可以为 <c>null</c>，表示「这一项不动」—— 这样只改地址时不必回传密钥。
/// </para>
/// </summary>
public sealed record IterationSettingsUpdate(
    /// <summary>迭代服务地址；留空表示不改动。要**关停**这个功能请用 <see cref="ClearBaseUrl"/>。</summary>
    string? BaseUrl = null,
    /// <summary>新的管理员密钥；留空表示沿用现有的（不会因为只改地址就把密钥抹掉）。</summary>
    string? AdminKey = null,
    /// <summary>显式清空密钥。与 <see cref="AdminKey"/> 同时给出时以清空为准。</summary>
    bool ClearAdminKey = false,
    /// <summary>显式把地址清空 —— 等价于「关掉改进建议功能」，页面会恢复成"还没配置"的提示。</summary>
    bool ClearBaseUrl = false,
    int? TimeoutSeconds = null);

/// <summary>「测试连接」的结果。</summary>
public sealed record IterationProbeResult(bool Ok, string Message);

/// <summary>
/// 配置存储：读写服务器上的**配置文件**（<c>config/iteration.json</c>，由编排挂载出来），
/// 密钥**不进数据库**。
/// </summary>
public interface IIterationSettingsStore
{
    /// <summary>读当前生效配置（配置文件优先，没有文件则沿用部署配置）。</summary>
    Task<IterationSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>保存配置到配置文件；下一个请求立即生效，无需重启。</summary>
    Task<Result<IterationSettingsSnapshot>> SaveAsync(
        IterationSettingsUpdate update,
        CancellationToken cancellationToken = default);
}
