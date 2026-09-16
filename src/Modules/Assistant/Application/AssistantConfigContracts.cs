namespace QiaoMES.Assistant.Application;

/// <summary>
/// 智能问数的**生效配置**快照（不是数据库原样，而是"当前真正在用的值"）。<para>
/// 刻意不返回 API Key 明文：只给 <see cref="HasApiKey"/> 与掩码，页面据此显示「已配置 sk-****abcd」。
/// </para>
/// </summary>
public sealed record AssistantSettingsSnapshot(
    bool Enabled,
    string BaseUrl,
    string Model,
    bool HasApiKey,
    string? ApiKeyMasked,
    int LlmTimeoutSeconds,
    int MaxRows,
    int QueryTimeoutSeconds,
    int MaxRepairAttempts,
    /// <summary><c>database</c> = 有人在页面上配过；<c>configuration</c> = 仍在用 appsettings / 环境变量。</summary>
    string Source,
    DateTime? UpdatedAt,
    string? UpdatedBy);

/// <summary>
/// 配置变更请求。<para>
/// 所有字段都可以为 <c>null</c>，表示「这一项不动」—— 这样前端只改模型名时不必回传其它项。
/// </para>
/// </summary>
public sealed record AssistantSettingsUpdate(
    bool? Enabled = null,
    string? BaseUrl = null,
    string? Model = null,
    /// <summary>新密钥；留空表示沿用现有的（不会因为只改模型名就把 Key 抹掉）。</summary>
    string? ApiKey = null,
    /// <summary>显式清空密钥。与 <see cref="ApiKey"/> 同时给出时以清空为准。</summary>
    bool ClearApiKey = false,
    int? LlmTimeoutSeconds = null,
    int? MaxRows = null,
    int? QueryTimeoutSeconds = null,
    int? MaxRepairAttempts = null);

/// <summary>「测试连接」的结果。</summary>
public sealed record AssistantProbeResult(bool Ok, string Message, string? Model);

/// <summary>
/// 配置存储：把库里的配置**写进那个单例 <c>AssistantOptions</c>**（运行时即时生效），
/// 并对外提供快照与保存。
/// </summary>
public interface IAssistantSettingsStore
{
    /// <summary>读当前生效配置（只读，不改状态）。</summary>
    Task<AssistantSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>保存配置；成功后立即应用到运行时options，无需重启。</summary>
    Task<Shared.Result<AssistantSettingsSnapshot>> SaveAsync(
        AssistantSettingsUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>启动时调用：把库里已有的配置应用到运行时（没有配置行则沿用 appsettings）。</summary>
    Task ApplyPersistedAsync(CancellationToken cancellationToken = default);
}

/// <summary>智能问数配置的应用服务（给管理页面用）。</summary>
public interface IAssistantConfigService
{
    Task<AssistantSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default);

    Task<Shared.Result<AssistantSettingsSnapshot>> SaveAsync(
        AssistantSettingsUpdate update,
        CancellationToken cancellationToken = default);

    /// <summary>用当前配置向模型发一个最小请求，验证「地址 / 密钥 / 模型名」是否真的能用。</summary>
    Task<AssistantProbeResult> TestAsync(CancellationToken cancellationToken = default);
}
