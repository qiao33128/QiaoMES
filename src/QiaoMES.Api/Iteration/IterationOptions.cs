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

    /// <summary>
    /// 本环境没配地址时，在「迭代服务还没配置…」那句提示**后面**补一句说明。
    /// 默认空 = 什么都不补（生产的正常状态就是这个）。<br/>
    /// 为什么需要它：开发环境是**刻意**不接自迭代服务的（数据隔离 —— 它上面跑的是 AI 改完还没人看过的代码，
    /// 接上去就可能有人在开发站点上批准/否决**真实**的迭代计划）。但后端只知道"地址为空"，
    /// 于是页面统一提示"请在配置里填上地址" —— 那句话在开发环境里是**误导**，照着填反而有害。
    /// 所以让那份环境用自己的配置把话说清楚，而不是在校验逻辑里判断"我是不是开发环境"。
    /// </summary>
    public string UnconfiguredHint { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
