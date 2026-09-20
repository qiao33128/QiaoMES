using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Api.Iteration;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>
/// 改进建议与迭代审阅的入口。放在组合根（与 <see cref="DashboardController"/> 同理）：
/// 它不实现业务，只做**权限闸门 + 转发**给 AI 迭代服务。<br/>
/// <para>
/// 为什么不把建议/计划/审阅做成 QiaoMES 自己的模块与数据表：
/// 这些数据属于迭代服务（它才是执行与审计的主体），宿主再存一份就必然出现两份真相。
/// 宿主的职责是「谁能在什么范围内提」——也就是权限，那正是 QiaoMES 已有的能力。
/// </para>
/// </summary>
[ApiController]
[Route("api/iteration")]
[Authorize]
public sealed class IterationController(
    IterationClient client,
    IAuthorizationService authorization) : ControllerBase
{
    /// <summary>当前迭代周期 + 本期计划条目 + 最近一次一致性检查结论。能提建议的人都能看（透明是这套机制能被信任的前提）。</summary>
    [HttpGet("cycle")]
    [HasPermission(Permissions.Iteration.Suggest)]
    public async Task<IActionResult> GetCycle(CancellationToken cancellationToken)
        => Respond(await client.GetCycleAsync(cancellationToken));

    /// <summary>
    /// 提交改进建议。<br/>
    /// 🔴 权限规则（由用户在需求里定）：<br/>
    /// · <c>Modify</c>（改现有功能）—— 只要有 <c>iteration:suggest</c> 就能提，但**只能对自己可用的功能提**：
    ///   请求必须带 <c>sourcePermission</c>（该功能的鉴权权限码），后端校验当前用户确实持有它；<br/>
    /// · <c>New</c>（新增功能）—— 只有管理员（<c>iteration:manage</c>）能提。
    /// </summary>
    [HttpPost("suggestions")]
    [HasPermission(Permissions.Iteration.Suggest)]
    public async Task<IActionResult> Submit([FromBody] SuggestionRequest request, CancellationToken cancellationToken)
    {
        var scopeError = await CheckScopeAsync(request, cancellationToken);
        if (scopeError is not null)
        {
            return ApiResults.Problem(scopeError);
        }

        var (actorId, actorName) = CurrentActor();

        return Respond(await client.SubmitAsync(
            request with { ActorId = actorId, ActorName = actorName }, cancellationToken));
    }

    /// <summary>审阅计划条目：批准 / 否决 / 提意见（note 非空则表示该条目本期先顺延，等按意见完善）。</summary>
    [HttpPost("plan-items/{itemId:guid}/review")]
    [HasPermission(Permissions.Iteration.Manage)]
    public async Task<IActionResult> Review(Guid itemId, [FromBody] ReviewRequest request, CancellationToken cancellationToken)
    {
        var (_, actorName) = CurrentActor();

        return Respond(await client.ReviewAsync(itemId, request with { Actor = actorName }, cancellationToken));
    }

    /// <summary>推进周期：collect 期外的三个动作 —— freeze（冻结收集）/ settle（结算）/ execute（执行已放行条目）。</summary>
    /// <remarks>
    /// 🔴 路由参数**不能**叫 <c>action</c>：在 MVC 属性路由里它是保留名，会被当成"动作名"参与动作选择 ——
    /// 于是 <c>/cycles/current/freeze</c> 被理解成"找一个名为 freeze 的动作"，直接 404（而且日志里只有一行 0ms 的 404，毫无线索）。
    /// 改叫 <c>verb</c> 就正常了。
    /// </remarks>
    [HttpPost("cycles/current/{verb}")]
    [HasPermission(Permissions.Iteration.Manage)]
    public async Task<IActionResult> Advance(string verb, CancellationToken cancellationToken)
    {
        if (verb is not ("freeze" or "settle" or "execute"))
        {
            return ApiResults.Problem(Error.Validation("Iteration.UnknownAction", $"不支持的周期动作：{verb}"));
        }

        return Respond(await client.AdvanceAsync(verb, cancellationToken));
    }

    /// <summary>
    /// 建议范围校验。<br/>
    /// 复用项目现成的 <c>perm:</c> 动态策略（<see cref="HasPermissionAttribute.PolicyPrefix"/>）来判断"当前用户有没有这个功能的权限" ——
    /// 权限来源与授权处理器完全一致（按 userId 查库 + 短缓存），不另造一套判断，避免两套口径打架。
    /// </summary>
    private async Task<Error?> CheckScopeAsync(SuggestionRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Category, "New", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.SourcePermission) || !Permissions.IsDefined(request.SourcePermission))
            {
                return Error.Validation(
                    "Iteration.MissingScope",
                    "缺少功能范围（sourcePermission）：需要指明这条建议是针对哪个功能提的。");
            }

            var allowed = await authorization.AuthorizeAsync(
                User, resource: null, HasPermissionAttribute.PolicyPrefix + request.SourcePermission);

            return allowed.Succeeded
                ? null
                : Error.Forbidden("Iteration.OutOfScope", "只能对自己可用的功能提修改建议；要提「新增功能」的建议请找管理员。");
        }

        var canManage = await authorization.AuthorizeAsync(
            User, resource: null, HasPermissionAttribute.PolicyPrefix + Permissions.Iteration.Manage);

        return canManage.Succeeded
            ? null
            : Error.Forbidden("Iteration.NewNeedsAdmin", "「新增功能」的建议只有管理员可以提出。");
    }

    private (string Id, string Name) CurrentActor()
    {
        var id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value
                 ?? "unknown";

        var name = User.Identity?.Name
                   ?? User.FindFirst("unique_name")?.Value
                   ?? id;

        return (id, name);
    }

    private IActionResult Respond(IterationResult result)
        => result.Ok
            ? Ok(result.Payload)
            : ApiResults.Problem(Error.Validation("Iteration.Unavailable", result.Error ?? "迭代服务调用失败"));
}
