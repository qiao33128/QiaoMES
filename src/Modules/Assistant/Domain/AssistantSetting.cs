using QiaoMES.Shared;

namespace QiaoMES.Assistant.Domain;

/// <summary>
/// 智能问数的运行时配置（**单行表**：全库只有一行，改配置就是改这一行）。
/// <para>
/// 为什么要有它：原先只能改 <c>appsettings.json</c> 或环境变量，改一次就要重新部署，
/// 而大模型 Key / 模型名恰恰是最常调整的东西。有了这张表，管理员可以在页面上直接配、立即生效。
/// </para>
/// <para>
/// 优先级：**库里存了行就以库为准**，没有行才用配置文件的默认值（见 <c>AssistantSettingsStore</c>）。
/// </para>
/// </summary>
public class AssistantSetting : Entity
{
    /// <summary>单行表的固定主键。</summary>
    public static readonly Guid SingletonKey = Guid.Parse("a55157a0-0000-4000-8000-000000000001");

    private AssistantSetting() { }

    public AssistantSetting(
        bool enabled,
        string? llmBaseUrl,
        string? llmModel,
        string? llmApiKeyProtected,
        int llmTimeoutSeconds,
        int maxRows,
        int queryTimeoutSeconds,
        int maxRepairAttempts,
        string? updatedBy)
        : base(SingletonKey)
    {
        Apply(enabled, llmBaseUrl, llmModel, llmApiKeyProtected, llmTimeoutSeconds, maxRows, queryTimeoutSeconds, maxRepairAttempts, updatedBy);
    }

    /// <summary>总开关。</summary>
    public bool Enabled { get; private set; }

    /// <summary>OpenAI 兼容端点，例如 <c>https://api.deepseek.com/v1</c>。</summary>
    public string? LlmBaseUrl { get; private set; }

    public string? LlmModel { get; private set; }

    /// <summary>
    /// 加密后的 API Key（见 <see cref="SecretProtector"/>）。<para>
    /// **明文既不落库、也不出接口**：页面只能看到掩码，改配置时留空即表示沿用旧值。
    /// </para>
    /// </summary>
    public string? LlmApiKeyProtected { get; private set; }

    public int LlmTimeoutSeconds { get; private set; }

    public int MaxRows { get; private set; }

    public int QueryTimeoutSeconds { get; private set; }

    public int MaxRepairAttempts { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>最后修改人（用户名，便于追责）。</summary>
    public string? UpdatedBy { get; private set; }

    public void Update(
        bool enabled,
        string? llmBaseUrl,
        string? llmModel,
        string? llmApiKeyProtected,
        int llmTimeoutSeconds,
        int maxRows,
        int queryTimeoutSeconds,
        int maxRepairAttempts,
        string? updatedBy)
        => Apply(enabled, llmBaseUrl, llmModel, llmApiKeyProtected, llmTimeoutSeconds, maxRows, queryTimeoutSeconds, maxRepairAttempts, updatedBy);

    private void Apply(
        bool enabled,
        string? llmBaseUrl,
        string? llmModel,
        string? llmApiKeyProtected,
        int llmTimeoutSeconds,
        int maxRows,
        int queryTimeoutSeconds,
        int maxRepairAttempts,
        string? updatedBy)
    {
        Enabled = enabled;
        LlmBaseUrl = Normalize(llmBaseUrl);
        LlmModel = Normalize(llmModel);
        LlmApiKeyProtected = string.IsNullOrWhiteSpace(llmApiKeyProtected) ? null : llmApiKeyProtected;
        LlmTimeoutSeconds = llmTimeoutSeconds;
        MaxRows = maxRows;
        QueryTimeoutSeconds = queryTimeoutSeconds;
        MaxRepairAttempts = maxRepairAttempts;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = Normalize(updatedBy);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
