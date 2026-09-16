using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Reporting.Infrastructure;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Reporting.Api.Controllers;

/// <summary>
/// 指标报表：OEE、达成率、直通率 / 一次合格率、产量与良率、不良 TOP N、停机 Pareto，支持 CSV 导出。
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(
    IMetricsService service,
    IMetricsAggregator aggregator) : ControllerBase
{
    /// <summary>
    /// 手动重算预聚合汇总（历史日期补齐 / 数据修正后刷新）。<para>
    /// 看板与报表默认读 `daily_shift_metrics`，未覆盖时自动回退实时聚合。
    /// </para>
    /// </summary>
    [HttpPost("rebuild-metrics")]
    [HasPermission(Permissions.Reporting.Manage)]
    public async Task<IActionResult> RebuildMetrics(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.Now);
        var start = from ?? end.AddDays(-6);

        var shifts = await aggregator.RebuildAsync(start, end, lineName, cancellationToken);
        var lastComputedAt = await aggregator.GetLastComputedAtAsync(cancellationToken);

        return Ok(new { from = start, to = end, shiftsRebuilt = shifts, lastComputedAt });
    }

    /// <summary>OEE = 可用率 × 性能 × 良率（计划时间按班次与生产日历推算）。</summary>
    [HttpGet("oee")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetOee(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetOeeAsync(new MetricsRangeRequest(from, to, lineName), cancellationToken));

    /// <summary>按班次汇总产量与良率（跨天夜班归属其生产日）。</summary>
    [HttpGet("shifts")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetShiftMetrics(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetShiftMetricsAsync(new MetricsRangeRequest(from, to, lineName), cancellationToken));

    [HttpGet("quality")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetQualityMetrics(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        [FromQuery] int topDefects = 10,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetQualityMetricsAsync(
            new MetricsRangeRequest(from, to, lineName), topDefects, cancellationToken));

    [HttpGet("achievement")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetAchievement(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        [FromQuery] int topOrders = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetAchievementAsync(
            new MetricsRangeRequest(from, to, lineName), topOrders, cancellationToken));

    [HttpGet("downtime")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetDowntime(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        [FromQuery] int topReasons = 10,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetDowntimeAsync(
            new MetricsRangeRequest(from, to, lineName), topReasons, cancellationToken));

    /// <summary>
    /// 导出 CSV（UTF-8 BOM，Excel 直接打开不乱码）。
    /// <c>type</c> 取值：<c>oee</c> / <c>shift</c> / <c>quality</c> / <c>achievement</c> / <c>downtime</c>。
    /// </summary>
    [HttpGet("export")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> Export(
        [FromQuery] string type = "oee",
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MetricsRangeRequest(from, to, lineName);
        var builder = new StringBuilder();

        switch (type.Trim().ToLowerInvariant())
        {
            case "shift":
            {
                var result = await service.GetShiftMetricsAsync(request, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                builder.AppendLine("生产日,班次代码,班次,产线,开始时间,结束时间,投产SN,完工,报废,良率%,检验单,合格,不合格,一次合格率FPY%");
                foreach (var item in result.Value.Items)
                {
                    builder.AppendLine(Csv(
                        item.ProductionDate.ToString("yyyy-MM-dd"),
                        item.ShiftCode,
                        item.ShiftName,
                        item.LineName ?? string.Empty,
                        item.StartAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        item.EndAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        item.TotalSn,
                        item.CompletedSn,
                        item.ScrappedSn,
                        item.YieldRate,
                        item.InspectionTotal,
                        item.InspectionPassed,
                        item.InspectionFailed,
                        item.Fpy));
                }

                break;
            }

            case "quality":
            {
                var result = await service.GetQualityMetricsAsync(request, 50, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                builder.AppendLine("项目,数值");
                builder.AppendLine(Csv("检验单总数", result.Value.InspectionTotal));
                builder.AppendLine(Csv("合格", result.Value.Passed));
                builder.AppendLine(Csv("不合格", result.Value.Failed));
                builder.AppendLine(Csv("让步接收", result.Value.Concessioned));
                builder.AppendLine(Csv("不良数", result.Value.DefectQuantity));
                builder.AppendLine(Csv("一次合格率FPY%", result.Value.Fpy));
                builder.AppendLine();
                builder.AppendLine("不良代码,出现次数");
                foreach (var defect in result.Value.TopDefects)
                {
                    builder.AppendLine(Csv(defect.DefectCode, defect.Count));
                }

                break;
            }

            case "achievement":
            {
                var result = await service.GetAchievementAsync(request, 200, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                builder.AppendLine(Csv("汇总", $"工单数 {result.Value.OrderCount}", $"计划 {result.Value.PlannedQuantity}", $"完工 {result.Value.CompletedQuantity}", $"达成率 {result.Value.AchievementRate}%"));
                builder.AppendLine("工单号,产品编码,计划数量,完工数量,达成率%,状态");
                foreach (var order in result.Value.Orders)
                {
                    builder.AppendLine(Csv(order.OrderNumber, order.ProductCode, order.PlannedQuantity, order.CompletedQuantity, order.AchievementRate, order.Status));
                }

                break;
            }

            case "downtime":
            {
                var result = await service.GetDowntimeAsync(request, 50, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                builder.AppendLine(Csv("总停机时长(分钟)", Math.Round(result.Value.TotalDownSeconds / 60d, 1), "停机次数", result.Value.DownCount));
                builder.AppendLine("停机原因,累计时长(分钟),次数");
                foreach (var item in result.Value.ByReason)
                {
                    builder.AppendLine(Csv(item.ReasonCode, Math.Round(item.TotalSeconds / 60d, 1), item.Count));
                }

                break;
            }

            default:
            {
                var result = await service.GetOeeAsync(request, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                builder.AppendLine($"OEE 报表,{result.Value.From:yyyy-MM-dd} ~ {result.Value.To:yyyy-MM-dd}");
                builder.AppendLine("指标,数值");
                builder.AppendLine(Csv("计划生产时间(小时)", result.Value.PlannedHours));
                builder.AppendLine(Csv("停机时间(小时)", result.Value.DowntimeHours));
                builder.AppendLine(Csv("运行时间(小时)", result.Value.RunHours));
                builder.AppendLine(Csv("可用率%", result.Value.Availability));
                builder.AppendLine(Csv("性能%", result.Value.Performance));
                builder.AppendLine(Csv("良率%", result.Value.Quality));
                builder.AppendLine(Csv("OEE%", result.Value.Oee));
                builder.AppendLine(Csv("投产SN", result.Value.TotalSn));
                builder.AppendLine(Csv("完工SN", result.Value.CompletedSn));
                builder.AppendLine(Csv("报废SN", result.Value.ScrappedSn));
                builder.AppendLine(Csv("理论工时(秒)", result.Value.TheoreticalSeconds));
                builder.AppendLine(Csv("实际工时(秒)", result.Value.ActualSeconds));

                break;
            }
        }

        // UTF-8 BOM：Excel 打开中文不乱码（GetBytes 不会自动带 BOM，必须手动前置）
        var preamble = Encoding.UTF8.GetPreamble();
        var bodyBytes = Encoding.UTF8.GetBytes(builder.ToString());
        var bytes = new byte[preamble.Length + bodyBytes.Length];
        preamble.CopyTo(bytes, 0);
        bodyBytes.CopyTo(bytes, preamble.Length);

        return File(bytes, "text/csv; charset=utf-8", $"report-{type}-{DateTime.Now:yyyyMMddHHmm}.csv");
    }

    /// <summary>
    /// 打印视图：返回**自包含的 HTML**，供浏览器「打印 → 另存为 PDF」。<para>
    /// 刻意**不引入 PDF 生成库**（QuestPDF/iText 都要拖包、还要处理中文字体嵌入与分页）——
    /// 服务端只负责数据与版式，字体与分页交给浏览器；前端用 fetch 拿到 HTML 后写入新窗口打印，
    /// 因此能正常携带 JWT（新窗口直接打开 URL 是带不上鉴权头的）。
    /// </para>
    /// </summary>
    [HttpGet("print-html")]
    [HasPermission(Permissions.Reporting.Read)]
    public async Task<IActionResult> GetPrintHtml(
        [FromQuery] string type = "oee",
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? lineName = null,
        CancellationToken cancellationToken = default)
    {
        var request = new MetricsRangeRequest(from, to, lineName);
        var normalized = type.Trim().ToLowerInvariant();

        var title = normalized switch
        {
            "shift" => "按班次产量与质量报表",
            "quality" => "质量指标报表",
            "achievement" => "工单达成率报表",
            "downtime" => "停机分析报表",
            _ => "OEE 报表",
        };

        var scope = $"{request.From?.ToString("yyyy-MM-dd") ?? "最早"} ~ {request.To?.ToString("yyyy-MM-dd") ?? "今天"}"
                    + (string.IsNullOrWhiteSpace(lineName) ? " · 全部产线" : $" · 产线 {lineName}")
                    + $" · 生成于 {DateTime.Now:yyyy-MM-dd HH:mm}";

        string[] headers;
        var rows = new List<string[]>();

        switch (normalized)
        {
            case "shift":
            {
                var result = await service.GetShiftMetricsAsync(request, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                headers = ["生产日", "班次", "时间窗", "投产SN", "完工", "报废", "良率%", "检验单", "合格", "不合格", "FPY%"];
                rows.AddRange(result.Value.Items.Select(item => new[]
                {
                    item.ProductionDate.ToString("yyyy-MM-dd"),
                    $"{item.ShiftCode} {item.ShiftName}",
                    $"{item.StartAtUtc.ToLocalTime():MM-dd HH:mm}~{item.EndAtUtc.ToLocalTime():MM-dd HH:mm}",
                    item.TotalSn.ToString(),
                    item.CompletedSn.ToString(),
                    item.ScrappedSn.ToString(),
                    item.YieldRate.ToString("0.00"),
                    item.InspectionTotal.ToString(),
                    item.InspectionPassed.ToString(),
                    item.InspectionFailed.ToString(),
                    item.Fpy.ToString("0.00"),
                }));
                break;
            }

            case "quality":
            {
                var result = await service.GetQualityMetricsAsync(request, 20, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                headers = ["不良代码", "出现次数"];
                rows.AddRange(result.Value.TopDefects.Select(item => new[] { item.DefectCode, item.Count.ToString() }));
                break;
            }

            case "achievement":
            {
                var result = await service.GetAchievementAsync(request, 100, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                headers = ["工单号", "产品编码", "计划数量", "完工数量", "达成率%"];
                rows.AddRange(result.Value.Orders.Select(item => new[]
                {
                    item.OrderNumber,
                    item.ProductCode,
                    item.PlannedQuantity.ToString(),
                    item.CompletedQuantity.ToString(),
                    item.AchievementRate.ToString("0.00"),
                }));
                break;
            }

            case "downtime":
            {
                var result = await service.GetDowntimeAsync(request, 20, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                headers = ["停机原因", "累计时长(分)", "次数"];
                rows.AddRange(result.Value.ByReason.Select(item => new[]
                {
                    item.ReasonCode,
                    Math.Round(item.TotalSeconds / 60d, 1).ToString("0.0"),
                    item.Count.ToString(),
                }));
                break;
            }

            default:
            {
                var result = await service.GetOeeAsync(request, cancellationToken);
                if (result.IsFailure)
                {
                    return ApiResults.Problem(result.Error);
                }

                headers = ["指标", "数值"];
                var oee = result.Value;
                rows.AddRange(new[]
                {
                    new[] { "计划生产时间(小时)", oee.PlannedHours.ToString("0.00") },
                    new[] { "故障停机(小时)", oee.DowntimeHours.ToString("0.00") },
                    new[] { "运行时间(小时)", oee.RunHours.ToString("0.00") },
                    new[] { "可用率%", oee.Availability.ToString("0.00") },
                    new[] { "性能%", oee.Performance.ToString("0.00") },
                    new[] { "良率%", oee.Quality.ToString("0.00") },
                    new[] { "OEE%", oee.Oee.ToString("0.00") },
                    new[] { "投产 / 完工 / 报废", $"{oee.TotalSn} / {oee.CompletedSn} / {oee.ScrappedSn}" },
                    new[] { "理论工时 / 实际工时(小时)", $"{oee.TheoreticalSeconds / 3600d:0.00} / {oee.ActualSeconds / 3600d:0.00}" },
                });
                break;
            }
        }

        return Content(BuildPrintHtml(title, scope, headers, rows), "text/html; charset=utf-8");
    }

    /// <summary>生成打印友好的自包含 HTML（A4 版式 + 表头重复 + 斑马纹，可直接另存为 PDF）。</summary>
    private static string BuildPrintHtml(string title, string scope, string[] headers, IReadOnlyList<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\" />");
        builder.Append($"<title>{WebUtility.HtmlEncode(title)}</title><style>");
        builder.Append("@page{size:A4 landscape;margin:12mm}");
        builder.Append("body{font-family:'Microsoft YaHei','PingFang SC','Noto Sans CJK SC',sans-serif;color:#1f2937;margin:0;padding:8px}");
        builder.Append("h1{font-size:20px;margin:0 0 4px}");
        builder.Append(".scope{font-size:12px;color:#6b7280;margin-bottom:14px}");
        builder.Append("table{width:100%;border-collapse:collapse;font-size:12px}");
        builder.Append("th{background:#f3f4f6;text-align:left;padding:7px 9px;border:1px solid #d1d5db;font-weight:600}");
        builder.Append("td{padding:6px 9px;border:1px solid #e5e7eb}");
        builder.Append("tbody tr:nth-child(even){background:#fafafa}");
        builder.Append("thead{display:table-header-group}");   // 跨页重复表头
        builder.Append(".foot{margin-top:14px;font-size:11px;color:#9ca3af}");
        builder.Append("</style></head><body>");
        builder.Append($"<h1>{WebUtility.HtmlEncode(title)}</h1>");
        builder.Append($"<div class=\"scope\">{WebUtility.HtmlEncode(scope)} · 共 {rows.Count} 行</div>");
        builder.Append("<table><thead><tr>");

        foreach (var header in headers)
        {
            builder.Append($"<th>{WebUtility.HtmlEncode(header)}</th>");
        }

        builder.Append("</tr></thead><tbody>");

        if (rows.Count == 0)
        {
            builder.Append($"<tr><td colspan=\"{headers.Length}\" style=\"text-align:center;color:#9ca3af\">该区间没有数据</td></tr>");
        }

        foreach (var row in rows)
        {
            builder.Append("<tr>");
            foreach (var cell in row)
            {
                builder.Append($"<td>{WebUtility.HtmlEncode(cell)}</td>");
            }
            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
        builder.Append("<div class=\"foot\">QiaoMES · 数据来源于预聚合汇总表 / 实时聚合（未覆盖时自动回退）</div>");
        builder.Append("</body></html>");

        return builder.ToString();
    }

    /// <summary>拼一行 CSV（含逗号 / 引号的字段加引号并转义）。</summary>
    private static string Csv(params object[] values)
    {
        var cells = values.Select(value =>
        {
            var text = value switch
            {
                null => string.Empty,
                decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };

            return text.Contains(',') || text.Contains('"') || text.Contains('\n')
                ? $"\"{text.Replace("\"", "\"\"")}\""
                : text;
        });

        return string.Join(',', cells);
    }
}
