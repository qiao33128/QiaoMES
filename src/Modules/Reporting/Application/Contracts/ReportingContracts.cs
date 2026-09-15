namespace QiaoMES.Reporting.Application.Contracts;

// ---------------- 班次 ----------------

public record ShiftQueryRequest(bool? IsActive = null, string? LineName = null, int Page = 1, int PageSize = 50);

public record CreateShiftRequest(
    string Code,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? LineName = null,
    int Sequence = 0,
    string? Remark = null);

public record UpdateShiftRequest(
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? LineName = null,
    int Sequence = 0,
    string? Remark = null);

public record ShiftDto(
    Guid Id,
    string Code,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string? LineName,
    int Sequence,
    bool IsActive,
    /// <summary>是否跨天班次（如 20:30 ~ 次日 08:30）。</summary>
    bool CrossesMidnight,
    string? Remark,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>当前所处班次（含生产日）。</summary>
public record CurrentShiftDto(
    DateOnly ProductionDate,
    Guid ShiftId,
    string ShiftCode,
    string ShiftName,
    string? LineName,
    DateTime StartAt,
    DateTime EndAt,
    double DurationHours);

/// <summary>按班次统计的时间口径（UTC 边界，便于直接与库中 UTC 时间戳比较）。</summary>
public record ShiftRangeDto(
    DateOnly ProductionDate,
    string ShiftCode,
    string ShiftName,
    string? LineName,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    double DurationHours);

/// <summary>班次口径查询（缺省为最近 7 个生产日）。</summary>
public record ShiftRangeRequest(DateOnly? From = null, DateOnly? To = null, string? LineName = null);

// ---------------- 日历 ----------------

public record CalendarQueryRequest(DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 100);

public record UpsertCalendarDayRequest(DateOnly Date, bool IsWorkingDay, string? Name = null, string? Remark = null);

public record CalendarDayDto(
    Guid Id,
    DateOnly Date,
    bool IsWorkingDay,
    string? Name,
    string? Remark,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// ---------------- 指标报表 ----------------

public record MetricsRangeRequest(DateOnly? From = null, DateOnly? To = null, string? LineName = null);

/// <summary>
/// OEE 报告：<c>OEE = 可用率 × 性能 × 良率</c>。
/// </summary>
/// <param name="PlannedHours">计划生产时间（按班次 × 工作日）。</param>
/// <param name="DowntimeHours">设备故障停机时长。</param>
/// <param name="RunHours">实际运行时间 = 计划 - 停机。</param>
/// <param name="Availability">可用率 = 运行 / 计划。</param>
/// <param name="Performance">性能 = 理论工时 / 实际工时（按工序标准工时加权，上限 100%）。</param>
/// <param name="Quality">良率 = 完工 / (完工 + 报废)。</param>
public record OeeReportDto(
    DateOnly From,
    DateOnly To,
    double PlannedHours,
    double DowntimeHours,
    double RunHours,
    decimal Availability,
    decimal Performance,
    decimal Quality,
    decimal Oee,
    int TotalSn,
    int CompletedSn,
    int ScrappedSn,
    long TheoreticalSeconds,
    long ActualSeconds);

/// <summary>按班次汇总（跨天夜班归属其生产日）。</summary>
public record ShiftMetricsDto(
    DateOnly ProductionDate,
    string ShiftCode,
    string ShiftName,
    string? LineName,
    DateTime StartAtUtc,
    DateTime EndAtUtc,
    int TotalSn,
    int CompletedSn,
    int ScrappedSn,
    decimal YieldRate,
    int InspectionTotal,
    int InspectionPassed,
    int InspectionFailed,
    decimal Fpy);

public record ShiftMetricsReportDto(IReadOnlyList<ShiftMetricsDto> Items, DateTime GeneratedAt);

public record DefectTopDto(string DefectCode, int Count);

public record QualityMetricsReportDto(
    int InspectionTotal,
    int Passed,
    int Failed,
    int Concessioned,
    int DefectQuantity,
    decimal Fpy,
    IReadOnlyList<DefectTopDto> TopDefects);

public record OrderAchievementDto(
    string OrderNumber,
    string ProductCode,
    int PlannedQuantity,
    int CompletedQuantity,
    decimal AchievementRate,
    int Status);

/// <summary>达成率：计划产量 vs 实际完工。</summary>
public record AchievementReportDto(
    int OrderCount,
    int PlannedQuantity,
    int CompletedQuantity,
    decimal AchievementRate,
    IReadOnlyList<OrderAchievementDto> Orders);

public record DowntimeByReasonDto(string ReasonCode, long TotalSeconds, int Count);

/// <summary>停机分析：总时长 + 按原因 Pareto（时长 + 次数）。</summary>
public record DowntimeReportDto(long TotalDownSeconds, int DownCount, IReadOnlyList<DowntimeByReasonDto> ByReason);
