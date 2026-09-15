using Microsoft.EntityFrameworkCore;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Reporting.Infrastructure.Persistence;
using QiaoMES.Shared;

namespace QiaoMES.Reporting.Infrastructure;

/// <summary>
/// 指标体系实现：以「生产日 + 班次」为统一口径，直接对只读投影做 SQL 级聚合。
/// </summary>
public class MetricsService(ReportingDbContext db, IShiftService shiftService) : IMetricsService
{
    // 枚举值对齐各业务模块（避免跨模块引用 Domain）
    private const int EquipmentDown = 2;          // EquipmentStatus.Down
    private const int SnCompleted = 1;            // SerialNumberStatus.Completed
    private const int SnScrapped = 2;             // SerialNumberStatus.Scrapped
    private const int InspectionPassed = 2;       // InspectionStatus.Passed
    private const int InspectionFailed = 3;       // InspectionStatus.Failed
    private const int InspectionConcessioned = 4;  // InspectionStatus.Concessioned

    public async Task<Result<OeeReportDto>> GetOeeAsync(
        MetricsRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var window = await ResolveWindowAsync(request, cancellationToken);

        var segments = await LoadDowntimeSegmentsAsync(window.StartUtc, window.EndUtc, cancellationToken);
        var downtimeSeconds = segments.Sum(s => s.DurationSeconds);

        var plannedSeconds = window.PlannedHours * 3600;
        var runSeconds = Math.Max(plannedSeconds - downtimeSeconds, 0);

        // 产量与良率：SN 状态分布
        var snGroups = await db.SerialNumbers
            .AsNoTracking()
            .Where(s => s.CreatedAt >= window.StartUtc && s.CreatedAt < window.EndUtc)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalSn = snGroups.Sum(x => x.Count);
        var completedSn = snGroups.FirstOrDefault(x => x.Status == SnCompleted)?.Count ?? 0;
        var scrappedSn = snGroups.FirstOrDefault(x => x.Status == SnScrapped)?.Count ?? 0;

        // 性能：工序标准工时 vs 实际工时
        var operations = await (from operation in db.WorkOrderOperations.AsNoTracking()
                                join order in db.WorkOrders.AsNoTracking() on operation.WorkOrderId equals order.Id
                                where order.CreatedAt >= window.StartUtc && order.CreatedAt < window.EndUtc
                                select new
                                {
                                    operation.StandardSeconds,
                                    operation.ActualSeconds,
                                    operation.GoodQuantity,
                                })
            .ToListAsync(cancellationToken);

        var theoreticalSeconds = operations.Sum(o => (long)o.StandardSeconds * o.GoodQuantity);
        var actualSeconds = operations.Sum(o => (long)o.ActualSeconds);

        var availability = plannedSeconds > 0
            ? Math.Round((decimal)(runSeconds / plannedSeconds) * 100m, 2)
            : 0m;
        var performance = actualSeconds > 0
            ? Math.Min(100m, Math.Round((decimal)theoreticalSeconds / actualSeconds * 100m, 2))
            : 0m;
        var quality = completedSn + scrappedSn > 0
            ? Math.Round((decimal)completedSn / (completedSn + scrappedSn) * 100m, 2)
            : 0m;
        var oee = Math.Round(availability * performance * quality / 10000m, 2);

        return Result.Success(new OeeReportDto(
            window.From,
            window.To,
            Math.Round(window.PlannedHours, 2),
            Math.Round(downtimeSeconds / 3600d, 2),
            Math.Round(runSeconds / 3600d, 2),
            availability,
            performance,
            quality,
            oee,
            totalSn,
            completedSn,
            scrappedSn,
            theoreticalSeconds,
            actualSeconds));
    }

    public async Task<Result<ShiftMetricsReportDto>> GetShiftMetricsAsync(
        MetricsRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var window = await ResolveWindowAsync(request, cancellationToken);
        var items = new List<ShiftMetricsDto>();

        foreach (var range in window.Ranges)
        {
            var snGroups = await db.SerialNumbers
                .AsNoTracking()
                .Where(s => s.CreatedAt >= range.StartAtUtc && s.CreatedAt < range.EndAtUtc)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var totalSn = snGroups.Sum(x => x.Count);
            var completedSn = snGroups.FirstOrDefault(x => x.Status == SnCompleted)?.Count ?? 0;
            var scrappedSn = snGroups.FirstOrDefault(x => x.Status == SnScrapped)?.Count ?? 0;

            var inspectionGroups = await db.Inspections
                .AsNoTracking()
                .Where(i => i.CreatedAt >= range.StartAtUtc && i.CreatedAt < range.EndAtUtc)
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var inspectionTotal = inspectionGroups.Sum(x => x.Count);
            var passed = inspectionGroups.FirstOrDefault(x => x.Status == InspectionPassed)?.Count ?? 0;
            var failed = inspectionGroups.FirstOrDefault(x => x.Status == InspectionFailed)?.Count ?? 0;
            var concessioned = inspectionGroups.FirstOrDefault(x => x.Status == InspectionConcessioned)?.Count ?? 0;

            var judged = passed + failed + concessioned;
            var fpy = judged == 0 ? 0m : Math.Round((decimal)passed / judged * 100m, 2);
            var finished = completedSn + scrappedSn;
            var yieldRate = finished == 0 ? 0m : Math.Round((decimal)completedSn / finished * 100m, 2);

            items.Add(new ShiftMetricsDto(
                range.ProductionDate,
                range.ShiftCode,
                range.ShiftName,
                range.LineName,
                range.StartAtUtc,
                range.EndAtUtc,
                totalSn,
                completedSn,
                scrappedSn,
                yieldRate,
                inspectionTotal,
                passed,
                failed,
                fpy));
        }

        return Result.Success(new ShiftMetricsReportDto(items, DateTime.UtcNow));
    }

    public async Task<Result<QualityMetricsReportDto>> GetQualityMetricsAsync(
        MetricsRangeRequest request,
        int topDefects = 10,
        CancellationToken cancellationToken = default)
    {
        var window = await ResolveWindowAsync(request, cancellationToken);

        var groups = await db.Inspections
            .AsNoTracking()
            .Where(i => i.CreatedAt >= window.StartUtc && i.CreatedAt < window.EndUtc)
            .GroupBy(i => i.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Defects = g.Sum(x => x.DefectQuantity),
            })
            .ToListAsync(cancellationToken);

        var total = groups.Sum(x => x.Count);
        var passed = groups.FirstOrDefault(x => x.Status == InspectionPassed)?.Count ?? 0;
        var failed = groups.FirstOrDefault(x => x.Status == InspectionFailed)?.Count ?? 0;
        var concessioned = groups.FirstOrDefault(x => x.Status == InspectionConcessioned)?.Count ?? 0;
        var defectQuantity = groups.Sum(x => x.Defects);

        var judged = passed + failed + concessioned;
        var fpy = judged == 0 ? 0m : Math.Round((decimal)passed / judged * 100m, 2);

        var take = topDefects is < 1 or > 50 ? 10 : topDefects;
        var top = await (from item in db.InspectionItems.AsNoTracking()
                         join inspection in db.Inspections.AsNoTracking() on item.InspectionId equals inspection.Id
                         where inspection.CreatedAt >= window.StartUtc
                               && inspection.CreatedAt < window.EndUtc
                               && item.IsQualified == false
                               && item.DefectCode != null
                         group item by item.DefectCode into grouped
                         orderby grouped.Count() descending
                         select new DefectTopDto(grouped.Key!, grouped.Count()))
            .Take(take)
            .ToListAsync(cancellationToken);

        return Result.Success(new QualityMetricsReportDto(
            total,
            passed,
            failed,
            concessioned,
            defectQuantity,
            fpy,
            top));
    }

    public async Task<Result<AchievementReportDto>> GetAchievementAsync(
        MetricsRangeRequest request,
        int topOrders = 20,
        CancellationToken cancellationToken = default)
    {
        var window = await ResolveWindowAsync(request, cancellationToken);

        var orders = await db.WorkOrders
            .AsNoTracking()
            .Where(w => w.CreatedAt >= window.StartUtc && w.CreatedAt < window.EndUtc)
            .OrderByDescending(w => w.PlannedQuantity)
            .Take(topOrders is < 1 or > 200 ? 20 : topOrders)
            .Select(w => new
            {
                w.OrderNumber,
                w.ProductCode,
                w.PlannedQuantity,
                w.CompletedQuantity,
                w.Status,
            })
            .ToListAsync(cancellationToken);

        var details = orders
            .Select(w => new OrderAchievementDto(
                w.OrderNumber,
                w.ProductCode,
                w.PlannedQuantity,
                w.CompletedQuantity,
                w.PlannedQuantity == 0
                    ? 0m
                    : Math.Round((decimal)w.CompletedQuantity / w.PlannedQuantity * 100m, 2),
                w.Status))
            .ToList();

        var plannedTotal = orders.Sum(w => w.PlannedQuantity);
        var completedTotal = orders.Sum(w => w.CompletedQuantity);
        var rate = plannedTotal == 0
            ? 0m
            : Math.Round((decimal)completedTotal / plannedTotal * 100m, 2);

        return Result.Success(new AchievementReportDto(
            details.Count,
            plannedTotal,
            completedTotal,
            rate,
            details));
    }

    public async Task<Result<DowntimeReportDto>> GetDowntimeAsync(
        MetricsRangeRequest request,
        int topReasons = 10,
        CancellationToken cancellationToken = default)
    {
        var window = await ResolveWindowAsync(request, cancellationToken);
        var segments = await LoadDowntimeSegmentsAsync(window.StartUtc, window.EndUtc, cancellationToken);

        var take = topReasons is < 1 or > 50 ? 10 : topReasons;
        var byReason = segments
            .GroupBy(s => string.IsNullOrWhiteSpace(s.ReasonCode) ? "UNKNOWN" : s.ReasonCode!)
            .Select(g => new DowntimeByReasonDto(g.Key, g.Sum(s => s.DurationSeconds), g.Count()))
            .OrderByDescending(x => x.TotalSeconds)
            .Take(take)
            .ToList();

        return Result.Success(new DowntimeReportDto(
            segments.Sum(s => s.DurationSeconds),
            segments.Count,
            byReason));
    }

    // ---------------- 内部实现 ----------------

    private sealed record MetricsWindow(
        DateOnly From,
        DateOnly To,
        DateTime StartUtc,
        DateTime EndUtc,
        double PlannedHours,
        IReadOnlyList<ShiftRangeDto> Ranges);

    private sealed record DowntimeSegment(string? ReasonCode, DateTime StartAt, DateTime EndAt)
    {
        public long DurationSeconds => (long)(EndAt - StartAt).TotalSeconds;
    }

    /// <summary>把请求的生产日区间换算成班次时间窗（无班次配置时退化为自然日全天）。</summary>
    private async Task<MetricsWindow> ResolveWindowAsync(MetricsRangeRequest request, CancellationToken cancellationToken)
    {
        var rangesResult = await shiftService.GetRangesAsync(
            new ShiftRangeRequest(request.From, request.To, request.LineName), cancellationToken);

        if (rangesResult.IsSuccess && rangesResult.Value.Count > 0)
        {
            var ranges = rangesResult.Value;
            return new MetricsWindow(
                ranges.Min(r => r.ProductionDate),
                ranges.Max(r => r.ProductionDate),
                ranges.Min(r => r.StartAtUtc),
                ranges.Max(r => r.EndAtUtc),
                ranges.Sum(r => r.DurationHours),
                ranges);
        }

        var to = request.To ?? DateOnly.FromDateTime(DateTime.Now);
        var from = request.From ?? to;
        var start = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime();
        var end = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime();

        return new MetricsWindow(from, to, start, end, (end - start).TotalHours, []);
    }

    /// <summary>
    /// 从设备状态日志还原停机时间段并裁剪到窗口内。<para>
    /// 日志只记录「状态切换时刻」，停机时长 = 下一条切换时刻 - 本条切换时刻（仍在停机则算到窗口结束）。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<DowntimeSegment>> LoadDowntimeSegmentsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
        // 多取 7 天日志，保证跨越窗口起点的连续停机也能正确配对
        var logs = await db.EquipmentStatusLogs
            .AsNoTracking()
            .Where(l => l.ChangedAt >= from.AddDays(-7) && l.ChangedAt <= to)
            .OrderBy(l => l.EquipmentId)
            .ThenBy(l => l.ChangedAt)
            .ToListAsync(cancellationToken);

        var segments = new List<DowntimeSegment>();

        foreach (var group in logs.GroupBy(l => l.EquipmentId))
        {
            var ordered = group.OrderBy(l => l.ChangedAt).ToList();

            for (var index = 0; index < ordered.Count; index++)
            {
                var current = ordered[index];
                if (current.ToStatus != EquipmentDown)
                {
                    continue;
                }

                var start = current.ChangedAt < from ? from : current.ChangedAt;
                var end = index + 1 < ordered.Count ? ordered[index + 1].ChangedAt : to;
                if (end > to)
                {
                    end = to;
                }
                if (end <= start)
                {
                    continue;
                }

                segments.Add(new DowntimeSegment(current.ReasonCode, start, end));
            }
        }

        return segments;
    }
}
