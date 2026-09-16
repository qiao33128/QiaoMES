using Microsoft.Extensions.Logging;
using QiaoMES.Shared;

namespace QiaoMES.Assistant.Application;

/// <summary>
/// 智能问数配置的应用服务（管理页面用）。
/// <para>
/// 这里只做「取 / 存 / 试连」三件事，真正的持久化与运行时生效在
/// <see cref="IAssistantSettingsStore"/> 里。
/// </para>
/// </summary>
public sealed class AssistantConfigService(
    IAssistantSettingsStore store,
    ISqlGenerator sqlGenerator,
    ILogger<AssistantConfigService> logger) : IAssistantConfigService
{
    public Task<AssistantSettingsSnapshot> GetAsync(CancellationToken cancellationToken = default)
        => store.GetAsync(cancellationToken);

    public Task<Result<AssistantSettingsSnapshot>> SaveAsync(
        AssistantSettingsUpdate update,
        CancellationToken cancellationToken = default)
        => store.SaveAsync(update, cancellationToken);

    public async Task<AssistantProbeResult> TestAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await store.GetAsync(cancellationToken);

        if (!snapshot.Enabled)
        {
            return new AssistantProbeResult(false, "智能问数总开关处于关闭状态，先打开再测试。", snapshot.Model);
        }

        if (!sqlGenerator.IsConfigured)
        {
            var keyState = snapshot.HasApiKey ? "已填" : "未填（本地 Ollama 可留空）";
            return new AssistantProbeResult(
                false,
                $"配置不完整：地址「{snapshot.BaseUrl}」、模型「{snapshot.Model}」、密钥 {keyState}。",
                snapshot.Model);
        }

        logger.LogInformation("智能问数：开始测试模型连接（{BaseUrl} / {Model}）", snapshot.BaseUrl, snapshot.Model);
        return await sqlGenerator.PingAsync(cancellationToken);
    }
}
