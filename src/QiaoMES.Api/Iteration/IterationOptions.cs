namespace QiaoMES.Api.Iteration;

/// <summary>
/// 宿主侧接入 AI 迭代服务的配置（appsettings 的 <c>Iteration</c> 节）。<br/>
/// 🔴 <see cref="AdminKey"/> 由**宿主服务端代持**，绝不下发前端 ——
/// 这正是"由 QiaoMES 代理转发"而不是"让浏览器直连"的意义：管理员密钥不该出现在浏览器里。
/// </summary>
public sealed class IterationOptions
{
    public const string SectionName = "Iteration";

    /// <summary>迭代服务地址，如 <c>http://localhost:8091</c>。留空则本功能整体不可用（接口返回明确的提示）。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>迭代服务的管理员密钥（X-Admin-Key）。</summary>
    public string AdminKey { get; set; } = string.Empty;

    /// <summary>单次调用超时。提交建议要调大模型（评审 + 一致性检查），默认给足 180 秒。</summary>
    public int TimeoutSeconds { get; set; } = 180;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
