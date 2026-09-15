using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.Reporting.Application;

/// <summary>
/// 指标体系服务：OEE、达成率、直通率 / 一次合格率、产量与良率、不良 TOP N、停机 Pareto。<para>
/// 所有统计都以「生产日 + 班次」为口径（跨天夜班归属其开始日），由 <see cref="IShiftService"/> 统一换算出时间窗。
/// </para>
/// </summary>
public interface IMetricsService
{
    /// <summary>OEE = 可用率 × 性能 × 良率。</summary>
    Task<Result<OeeReportDto>> GetOeeAsync(MetricsRangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>按班次汇总产量与良率（跨天班次正确归属）。</summary>
    Task<Result<ShiftMetricsReportDto>> GetShiftMetricsAsync(MetricsRangeRequest request, CancellationToken cancellationToken = default);

    /// <summary>质量指标：一次合格率 FPY、让步接收、不良数、不良代码 TOP N。</summary>
    Task<Result<QualityMetricsReportDto>> GetQualityMetricsAsync(
        MetricsRangeRequest request,
        int topDefects = 10,
        CancellationToken cancellationToken = default);

    /// <summary>工单达成率：计划产量 vs 实际完工。</summary>
    Task<Result<AchievementReportDto>> GetAchievementAsync(
        MetricsRangeRequest request,
        int topOrders = 20,
        CancellationToken cancellationToken = default);

    /// <summary>停机分析：总停机时长 + 按原因 Pareto（时长 + 次数）。</summary>
    Task<Result<DowntimeReportDto>> GetDowntimeAsync(
        MetricsRangeRequest request,
        int topReasons = 10,
        CancellationToken cancellationToken = default);
}
