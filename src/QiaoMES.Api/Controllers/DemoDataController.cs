using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Api.Seed;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>
/// 演示数据(默认仅在 Development 环境可用)。
/// <para>
/// 用途:一条命令把「3 条 SMT 产线 → 产品 / BOM / 工艺路线 → 工单下达 → 工序报工 → SN 过站 →
/// 来料批次与 IQC → 检验与不合格处置 → Andon」整条链路的数据灌进库里,
/// 让所有页面、报表、SPC 与 OEE 立刻有内容可看。它同时也是「智能问数」的最佳练手数据集。
/// </para>
/// <para>
/// 设计约束:数据全部以 <c>DEMO-</c> 前缀标识,<c>DELETE</c> 可精确清理,绝不影响手工录入的业务数据;
/// 整个生成过程在一个 HTTP 事务内完成,失败整体回滚。
/// </para>
/// </summary>
[ApiController]
[Route("api/dev/demo-data")]
[Authorize]
public class DemoDataController(
    DemoDataSeeder seeder,
    IHostEnvironment environment,
    IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// 环境护栏:默认只在 Development 放行。<para>
    /// 如果确实要在演示服务器上开启(例如给别人看),显式设置配置项 <c>DemoData:Enabled = true</c>——
    /// 生产库不建议这么做。
    /// </para>
    /// </summary>
    private IActionResult? Guard()
        => environment.IsDevelopment() || configuration.GetValue("DemoData:Enabled", false)
            ? null
            : ApiResults.Problem(new Error(
                "DemoData.Disabled",
                "演示数据端点已关闭。默认仅在 Development 环境可用;"
                + "如确需在非开发环境使用,请显式设置配置项 DemoData:Enabled = true。",
                ErrorType.Forbidden));

    /// <summary>查看当前库里的演示数据条数(只读,可用来判断是否需要播种)。</summary>
    [HttpGet]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> Describe(CancellationToken cancellationToken)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        return Ok(new
        {
            environment = environment.EnvironmentName,
            prefix = DemoDataSeeder.Prefix,
            counts = await seeder.DescribeAsync(cancellationToken),
        });
    }

    /// <summary>
    /// 生成演示数据(幂等:先清理旧演示数据再重建)。<para>
    /// 参数建议保持默认;数据量越大耗时越长(默认规模约 10~30 秒),单次请求请留足超时时间。
    /// </para>
    /// </summary>
    [HttpPost]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> Seed(
        [FromQuery] int days = 30,
        [FromQuery] int workOrders = 8,
        [FromQuery] int snPerOrder = 50,
        CancellationToken cancellationToken = default)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        var summary = await seeder.ResetAsync(
            new DemoSeedOptions(days, workOrders, snPerOrder),
            cancellationToken);

        return Ok(summary);
    }

    /// <summary>清空全部演示数据(只删除 <c>DEMO-</c> 前缀的数据)。</summary>
    [HttpDelete]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> Cleanup(
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        if (Guard() is { } blocked)
        {
            return blocked;
        }

        // 删除后连带重算预聚合表,避免这一段窗口的报表还显示"演示期间的旧数字"
        var removed = await seeder.CleanupAsync(days, rebuildMetrics: true, cancellationToken);
        return Ok(new { removedRows = removed, note = "演示数据已清空,业务数据未受影响;预聚合指标已按该窗口重算。" });
    }
}
