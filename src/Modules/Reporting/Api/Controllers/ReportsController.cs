using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Reporting.Api.Controllers;

/// <summary>
/// 指标报表：OEE、达成率、直通率 / 一次合格率、产量与良率、不良 TOP N、停机 Pareto，支持 CSV 导出。
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController(IMetricsService service) : ControllerBase
{
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
