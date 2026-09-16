namespace QiaoMES.Assistant.Domain;

/// <summary>
/// 智能问数配置(绑定 <c>appsettings.json</c> 的 <c>Assistant</c> 节)。
/// <para>设计成纯 POCO 放在 Domain:Application 层可以直接构造注入,不需要额外依赖 <c>IOptions</c> 包。</para>
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>总开关。关闭时接口返回 503 并给出配置提示,避免"点了没反应"。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>数据集市:最多返回多少行(超过则截断并在结果里标记)。</summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>单条 SQL 的执行超时(秒)。</summary>
    public int QueryTimeoutSeconds { get; set; } = 15;

    /// <summary>SQL 生成失败后最多让模型自我修复几次。</summary>
    public int MaxRepairAttempts { get; set; } = 2;

    /// <summary>语义层缓存时长(分钟)。表结构变动不频繁,缓存可显著降低每次提问的 token 成本。</summary>
    public int SchemaCacheMinutes { get; set; } = 10;

    /// <summary>
    /// 问数专用连接串。<para>
    /// <b>强烈建议在生产环境指向「只读账号」</b>(见 <c>docs/AI-QUERY.md</c>);
    /// 留空则复用 <c>ConnectionStrings:DefaultDb</c>,此时依靠 <c>SET TRANSACTION READ ONLY</c> 兜底。
    /// </para>
    /// </summary>
    public string? ConnectionString { get; set; }

    public LlmOptions Llm { get; set; } = new();
}

/// <summary>大模型接入配置。任何 OpenAI 兼容端点都适用(DeepSeek / 通义 / Kimi / 硅基流动 / 本地 Ollama)。</summary>
public sealed class LlmOptions
{
    /// <summary>兼容 OpenAI 的 base url,例如 <c>https://api.deepseek.com/v1</c>、<c>http://localhost:11434/v1</c>。</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com/v1";

    /// <summary>API Key(本地 Ollama 可留空)。</summary>
    public string? ApiKey { get; set; }

    /// <summary>模型名,例如 <c>deepseek-chat</c>、<c>qwen-plus</c>、<c>qwen2.5-coder:14b</c>。</summary>
    public string Model { get; set; } = "deepseek-chat";

    /// <summary>单次请求超时(秒)。</summary>
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>采样温度。写 SQL 建议 0,保证稳定复现。</summary>
    public double Temperature { get; set; }
}
