using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Application.Contracts;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Assistant.Api.Controllers;

/// <summary>
/// 智能问数(自然语言查数)。
/// <para>
/// 链路:中文问题 → 语义层(表结构 + 业务口径)喂给大模型 → 生成只读 SQL
/// → <c>SqlGuard</c> 白名单校验 → 以 <c>READ ONLY</c> 事务执行 → 失败自动带错误让模型改一版。
/// </para>
/// <para>
/// 数据安全:模型永远拿不到数据库连接,只能拿到"表结构文本";即便模型乱写 SQL,
/// 也会被只读护栏与数据库的 <c>SET TRANSACTION READ ONLY</c> 双重拦下。
/// </para>
/// </summary>
[ApiController]
[Route("api/assistant")]
[Authorize]
public class AssistantController(
    IAssistantService assistantService,
    IAssistantConfigService configService,
    ISchemaProvider schemaProvider) : ControllerBase
{
    /// <summary>能力自检:是否启用、模型是否配好、语义层覆盖多少张表。前端用它给出明确提示。</summary>
    [HttpGet("status")]
    [HasPermission(Permissions.Assistant.Read)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
        => Ok(await assistantService.GetStatusAsync(cancellationToken));

    // ---------------- 模型配置（页面上可配，保存即生效） ----------------

    /// <summary>
    /// 读取智能问数的**生效配置**。<para>
    /// 密钥只回显掩码（如 <c>sk-a****wxyz</c>）与「是否已配置」，明文永不出接口。
    /// </para>
    /// </summary>
    [HttpGet("config")]
    [HasPermission(Permissions.Assistant.Manage)]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
        => Ok(await configService.GetAsync(cancellationToken));

    /// <summary>
    /// 保存配置。未提供的字段保持原值；<c>apiKey</c> 留空表示沿用现有密钥。<para>
    /// 保存后**立即生效**（写回运行时单例配置），无需重启或重新部署。
    /// </para>
    /// </summary>
    [HttpPut("config")]
    [HasPermission(Permissions.Assistant.Manage)]
    public async Task<IActionResult> SaveConfig(
        [FromBody] AssistantSettingsUpdate update,
        CancellationToken cancellationToken)
        => ApiResults.FromResult(await configService.SaveAsync(update, cancellationToken));

    /// <summary>用当前配置向模型发一个最小请求，确认「地址 / 密钥 / 模型名」真的能用。</summary>
    [HttpPost("config/test")]
    [HasPermission(Permissions.Assistant.Manage)]
    public async Task<IActionResult> TestConfig(CancellationToken cancellationToken)
        => Ok(await configService.TestAsync(cancellationToken));

    /// <summary>问答:返回生成的 SQL、结果集与推荐图表。</summary>
    [HttpPost("ask")]
    [HasPermission(Permissions.Assistant.Ask)]
    public async Task<IActionResult> Ask(
        [FromBody] AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return ApiResults.Problem(Error.Validation("Assistant.EmptyQuestion", "问题不能为空"));
        }

        return Ok(await assistantService.AskAsync(request, cancellationToken));
    }

    /// <summary>查看喂给模型的语义层文本(调试用:排查"模型为什么不懂这个字段")。</summary>
    [HttpGet("schema")]
    [HasPermission(Permissions.Assistant.Read)]
    public async Task<IActionResult> GetSchema(CancellationToken cancellationToken)
        => Ok(new
        {
            tableCount = await schemaProvider.GetTableCountAsync(cancellationToken),
            glossary = await schemaProvider.GetGlossaryTextAsync(cancellationToken),
            schema = await schemaProvider.GetSchemaTextAsync(cancellationToken),
        });
}
