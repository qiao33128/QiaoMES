using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Assistant.Infrastructure.Persistence;

/// <summary>
/// 配置存储实现。
/// <para>
/// 设计要点：**运行时的唯一事实来源始终是那个单例 <see cref="AssistantOptions"/>**，
/// 数据库只是"持久化 + 覆盖"它。这样所有消费方（SQL 生成器 / 只读执行器 / 编排服务）
/// 一行都不用改，也不需要 IOptionsMonitor —— 改完配置立刻生效。
/// </para>
/// <para>
/// 优先级：库里有配置行 → 以库为准（包括"密钥被清空"这种状态）；没有行 → 沿用 appsettings / 环境变量。
/// </para>
/// </summary>
public sealed class AssistantSettingsStore(
    AssistantOptions options,
    AssistantDbContext db,
    IConfiguration configuration,
    ICurrentUser currentUser,
    ILogger<AssistantSettingsStore> logger) : IAssistantSettingsStore
{
    private string Passphrase => configuration["Jwt:SecretKey"] ?? string.Empty;

    public async Task<AssistantSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await LoadRowAsync(tracking: false, cancellationToken);
        return Snapshot(row);
    }

    public async Task<Result<AssistantSettingsSnapshot>> SaveAsync(
        AssistantSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        // options 里永远是"当前生效值"，所以未提供的项直接沿用 options 即可
        var enabled = update.Enabled ?? options.Enabled;
        var baseUrl = Normalize(update.BaseUrl) ?? options.Llm.BaseUrl;
        var model = Normalize(update.Model) ?? options.Llm.Model;
        var llmTimeout = update.LlmTimeoutSeconds ?? options.Llm.TimeoutSeconds;
        var maxRows = update.MaxRows ?? options.MaxRows;
        var queryTimeout = update.QueryTimeoutSeconds ?? options.QueryTimeoutSeconds;
        var maxRepair = update.MaxRepairAttempts ?? options.MaxRepairAttempts;

        if (Validate(baseUrl, model, llmTimeout, maxRows, queryTimeout, maxRepair) is { } error)
        {
            return Result.Failure<AssistantSettingsSnapshot>(error);
        }

        var row = await LoadRowAsync(tracking: true, cancellationToken);

        // 密钥：留空 = 沿用现有值。
        // 这一步很重要 —— 否则"只改模型名"会把密钥抹掉，也会把用环境变量配好的 Key 在首次保存时清空。
        string? protectedKey;
        if (update.ClearApiKey)
        {
            protectedKey = null;
        }
        else if (!string.IsNullOrWhiteSpace(update.ApiKey))
        {
            protectedKey = SecretProtector.Protect(update.ApiKey.Trim(), Passphrase);
        }
        else
        {
            protectedKey = row?.LlmApiKeyProtected
                           ?? (string.IsNullOrWhiteSpace(options.Llm.ApiKey)
                               ? null
                               : SecretProtector.Protect(options.Llm.ApiKey!, Passphrase));
        }

        var actor = currentUser.Username ?? "unknown";

        if (row is null)
        {
            row = new AssistantSetting(
                enabled, baseUrl, model, protectedKey, llmTimeout, maxRows, queryTimeout, maxRepair, actor);
            db.Settings.Add(row);
        }
        else
        {
            row.Update(enabled, baseUrl, model, protectedKey, llmTimeout, maxRows, queryTimeout, maxRepair, actor);
        }

        await db.SaveChangesAsync(cancellationToken);

        // 立即生效：把库里这行写回运行时 options（不需要重启，也不需要 IOptionsMonitor）
        Apply(row, log: false);

        logger.LogInformation(
            "智能问数配置已更新（操作人 {Actor}，模型 {Model}，密钥 {KeyState}，总开关 {Enabled}）",
            actor, options.Llm.Model, protectedKey is null ? "已清空" : "已配置", options.Enabled);

        return Result.Success(Snapshot(row));
    }

    public async Task ApplyPersistedAsync(CancellationToken cancellationToken = default)
    {
        var row = await LoadRowAsync(tracking: false, cancellationToken);
        if (row is null)
        {
            logger.LogInformation("智能问数：数据库里还没有配置行，沿用 appsettings / 环境变量的配置");
            return;
        }

        Apply(row, log: true);
    }

    /// <summary>把配置行写进单例 options。</summary>
    private void Apply(AssistantSetting row, bool log)
    {
        options.Enabled = row.Enabled;

        if (!string.IsNullOrWhiteSpace(row.LlmBaseUrl))
        {
            options.Llm.BaseUrl = row.LlmBaseUrl!;
        }

        if (!string.IsNullOrWhiteSpace(row.LlmModel))
        {
            options.Llm.Model = row.LlmModel!;
        }

        options.Llm.TimeoutSeconds = row.LlmTimeoutSeconds;
        options.MaxRows = row.MaxRows;
        options.QueryTimeoutSeconds = row.QueryTimeoutSeconds;
        options.MaxRepairAttempts = row.MaxRepairAttempts;

        var plainKey = SecretProtector.Unprotect(row.LlmApiKeyProtected, Passphrase);
        if (row.LlmApiKeyProtected is not null && plainKey is null)
        {
            logger.LogWarning(
                "智能问数：数据库里的 API Key 解密失败（很可能更换过 Jwt:SecretKey）——请在「智能问数 → 模型配置」里重新填写");
        }

        // 注意：即使为 null 也要赋值 —— "库里清空了密钥"必须能覆盖掉环境变量里的密钥
        options.Llm.ApiKey = plainKey;

        if (log)
        {
            logger.LogInformation(
                "智能问数：已应用数据库配置（模型 {Model}，密钥 {KeyState}，行更新时间 {UpdatedAt:u}）",
                options.Llm.Model, plainKey is null ? "未配置" : "已配置", row.UpdatedAt);
        }
    }

    private AssistantSettingsSnapshot Snapshot(AssistantSetting? row)
    {
        var hasKey = !string.IsNullOrWhiteSpace(options.Llm.ApiKey);

        return new AssistantSettingsSnapshot(
            options.Enabled,
            options.Llm.BaseUrl,
            options.Llm.Model,
            hasKey,
            hasKey ? SecretProtector.Mask(options.Llm.ApiKey) : null,
            options.Llm.TimeoutSeconds,
            options.MaxRows,
            options.QueryTimeoutSeconds,
            options.MaxRepairAttempts,
            row is null ? "configuration" : "database",
            row?.UpdatedAt,
            row?.UpdatedBy);
    }

    private async Task<AssistantSetting?> LoadRowAsync(bool tracking, CancellationToken cancellationToken)
    {
        var query = db.Settings.AsQueryable();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(s => s.Id == AssistantSetting.SingletonKey, cancellationToken);
    }

    /// <summary>空串一律当"未提供"处理（前端清空输入框时传的就是空串）。</summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error? Validate(
        string baseUrl,
        string model,
        int llmTimeout,
        int maxRows,
        int queryTimeout,
        int maxRepair)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")))
        {
            return Error.Validation(
                "Assistant.InvalidBaseUrl",
                "模型地址必须是完整 URL，例如 https://api.deepseek.com/v1（本地 Ollama 用 http://host:11434/v1）");
        }

        if (!string.IsNullOrWhiteSpace(model) && model.Length > 100)
        {
            return Error.Validation("Assistant.InvalidModel", "模型名过长");
        }

        if (llmTimeout is < 10 or > 600)
        {
            return Error.Validation("Assistant.InvalidLlmTimeout", "模型超时必须在 10~600 秒之间");
        }

        if (maxRows is < 1 or > 2000)
        {
            return Error.Validation("Assistant.InvalidMaxRows", "返回行数上限必须在 1~2000 之间");
        }

        if (queryTimeout is < 1 or > 300)
        {
            return Error.Validation("Assistant.InvalidQueryTimeout", "SQL 超时必须在 1~300 秒之间");
        }

        if (maxRepair is < 0 or > 5)
        {
            return Error.Validation("Assistant.InvalidRepairAttempts", "自我修复轮数必须在 0~5 之间");
        }

        return null;
    }
}
