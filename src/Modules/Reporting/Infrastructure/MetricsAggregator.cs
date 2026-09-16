using Microsoft.EntityFrameworkCore;
using QiaoMES.Reporting.Application;
using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Reporting.Domain;
using QiaoMES.Reporting.Infrastructure.Persistence;

namespace QiaoMES.Reporting.Infrastructure;

/// <summary>
/// 预聚合器：把明细（SN / 检验单 / 工序工时 / 设备停机）按「生产日 + 班次」汇总进
/// <c>reporting.daily_shift_metrics</c>，供看板与报表快速读取。<para>
/// 重算按班次窗口逐个进行（班次数少，通常 2~3 个/天），天然正确处理跨天夜班归属。
/// </para>
/// </summary>
public interface IMetricsAggregator
{
    /// <summary>重算指定生产日区间的汇总（幂等 upsert），返回处理的班次数。</summary>
    Task<int> RebuildAsync(DateOnly from, DateOnly to, string? lineName, CancellationToken cancellationToken = default);

    /// <summary>读取汇总数据。</summary>
    Task<IReadOnlyList<DailyShiftMetric>> QueryAsync(
        DateOnly from,
        DateOnly to,
        string? lineName,
        CancellationToken cancellationToken = default);

    /// <summary>汇总表最近一次计算时间（无数据返回 null）。</summary>
    Task<DateTime?> GetLastComputedAtAsync(CancellationToken cancellationToken = default);
}

public sealed class MetricsAggregator(
    ReportingDbContext db,
    IShiftService shiftService) : IMetricsAggregator
{
    private const int SnCompleted = 1;
    private const int SnScrapped = 2;
    private const int SnInProcess = 0;
    private const int SnOnHold = 3;
    private const int InspectionPassed = 2;
    private const int InspectionFailed = 3;
    private const int InspectionConcessioned = 4;
    private const int EquipmentDown = 2;

    public async Task<int> RebuildAsync(
        DateOnly from,
        DateOnly to,
        string? lineName,
        CancellationToken cancellationToken = default)
    {
        var rangesResult = await shiftService.GetRangesAsync(new ShiftRangeRequest(from, to, lineName), cancellationToken);
        if (rangesResult.IsFailure || rangesResult.Value.Count == 0)
        {
            return 0;
        }

        var ranges = rangesResult.Value;
        var existing = await db.DailyShiftMetrics
            .Where(m => m.ProductionDate >= from && m.ProductionDate <= to)
            .ToListAsync(cancellationToken);

        // 停机时段按窗口整体还原一次，再按班次裁剪复用
        var downtimeSegments = await LoadDowntimeSegmentsAsync(
            ranges.Min(r => r.StartAtUtc),
            ranges.Max(r => r.EndAtUtc),
            cancellationToken);

        var processed = 0;

        foreach (var range in ranges)
        {
            var key = (range.LineName ?? string.Empty).Trim();

            var snGroups = await db.SerialNumbers
                .AsNoTracking()
                .Where(s => s.CreatedAt >= range.StartAtUtc && s.CreatedAt < range.EndAtUtc)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var inspectionGroups = await db.Inspections
                .AsNoTracking()
                .Where(i => i.CreatedAt >= range.StartAtUtc && i.CreatedAt < range.EndAtUtc)
                .GroupBy(i => i.Status)
                .Select(g => new { Status = g.Key, Count = g.Count(), Defects = g.Sum(x => x.DefectQuantity) })
                .ToListAsync(cancellationToken);

            var operations = await (from operation in db.WorkOrderOperations.AsNoTracking()
                                    join order in db.WorkOrders.AsNoTracking() on operation.WorkOrderId equals order.Id
                                    where order.CreatedAt >= range.StartAtUtc && order.CreatedAt < range.EndAtUtc
                                    select new { operation.StandardSeconds, operation.ActualSeconds, operation.GoodQuantity })
                .ToListAsync(cancellationToken);

            var downtime = downtimeSegments
                .Where(s => s.StartAt < range.EndAtUtc && s.EndAt > range.StartAtUtc)
                .ToList();

            var downtimeSeconds = downtime.Sum(s =>
                (long)((s.EndAt < range.EndAtUtc ? s.EndAt : range.EndAtUtc)
                       - (s.StartAt > range.StartAtUtc ? s.StartAt : range.StartAtUtc)).TotalSeconds);

            int Count(int[] statuses) => snGroups.Where(g => statuses.Contains(g.Status)).Sum(g => g.Count);

            var metric = existing.FirstOrDefault(m =>
                m.ProductionDate == range.ProductionDate
                && m.ShiftCode == range.ShiftCode
                && m.LineName == key);

            if (metric is null)
            {
                metric = new DailyShiftMetric(
                    range.ProductionDate, range.ShiftCode, range.ShiftName, key,
                    range.StartAtUtc, range.EndAtUtc, range.DurationHours);

                db.DailyShiftMetrics.Add(metric);
                existing.Add(metric);
            }

            metric.Update(
                range.ShiftName,
                range.StartAtUtc,
                range.EndAtUtc,
                range.DurationHours,
                snGroups.Sum(g => g.Count),
                Count([SnCompleted]),
                Count([SnScrapped]),
                Count([SnInProcess]),
                Count([SnOnHold]),
                inspectionGroups.Sum(g => g.Count),
                inspectionGroups.FirstOrDefault(g => g.Status == InspectionPassed)?.Count ?? 0,
                inspectionGroups.FirstOrDefault(g => g.Status == InspectionFailed)?.Count ?? 0,
                inspectionGroups.FirstOrDefault(g => g.Status == InspectionConcessioned)?.Count ?? 0,
                inspectionGroups.Sum(g => g.Defects),
                operations.Sum(o => (long)o.StandardSeconds * o.GoodQuantity),
                operations.Sum(o => (long)o.ActualSeconds),
                downtimeSeconds,
                downtime.Count);

            processed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    public async Task<IReadOnlyList<DailyShiftMetric>> QueryAsync(
        DateOnly from,
        DateOnly to,
        string? lineName,
        CancellationToken cancellationToken = default)
    {
        var query = db.DailyShiftMetrics
            .AsNoTracking()
            .Where(m => m.ProductionDate >= from && m.ProductionDate <= to);

        if (!string.IsNullOrWhiteSpace(lineName))
        {
            query = query.Where(m => m.LineName == lineName);
        }

        return await query
            .OrderBy(m => m.ProductionDate)
            .ThenBy(m => m.ShiftCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastComputedAtAsync(CancellationToken cancellationToken = default)
        => await db.DailyShiftMetrics
            .AsNoTracking()
            .OrderByDescending(m => m.ComputedAt)
            .Select(m => (DateTime?)m.ComputedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private sealed record DowntimeSegment(DateTime StartAt, DateTime EndAt);

    /// <summary>从设备状态日志还原停机时段（与 MetricsService 同口径）。</summary>
    private async Task<IReadOnlyList<DowntimeSegment>> LoadDowntimeSegmentsAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken)
    {
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
                if (ordered[index].ToStatus != EquipmentDown)
                {
                    continue;
                }

                var start = ordered[index].ChangedAt < from ? from : ordered[index].ChangedAt;
                var end = index + 1 < ordered.Count ? ordered[index + 1].ChangedAt : to;
                if (end > to)
                {
                    end = to;
                }
                if (end <= start)
                {
                    continue;
                }

                segments.Add(new DowntimeSegment(start, end));
            }
        }

        return segments;
    }
}
