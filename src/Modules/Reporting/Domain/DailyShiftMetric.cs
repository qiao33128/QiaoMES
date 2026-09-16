using QiaoMES.Shared;

namespace QiaoMES.Reporting.Domain;

/// <summary>
/// 日 / 班次预聚合指标（汇总表）。<para>
/// 背景：明细表上的区间聚合会随数据量**线性劣化**（实测 100 万行 SN 的 7 天窗口 P95 已达 384ms，
/// 且加索引无效——优化器判定顺序扫描更快）。看板与报表因此统一改读本表：
/// 把「每次扫几十万行」变成「读几十行」，同时把分析负载与生产写入彻底分开。
/// </para>
/// <para>
/// 聚合键：<c>生产日 + 班次代码 + 产线</c>（产线为空串表示全局维度）。
/// </para>
/// </summary>
public class DailyShiftMetric : Entity
{
    private DailyShiftMetric() { }

    public DailyShiftMetric(
        DateOnly productionDate,
        string shiftCode,
        string shiftName,
        string lineName,
        DateTime startAtUtc,
        DateTime endAtUtc,
        double plannedHours)
        : base(Guid.NewGuid())
    {
        ProductionDate = productionDate;
        ShiftCode = shiftCode.Trim();
        ShiftName = shiftName.Trim();
        LineName = (lineName ?? string.Empty).Trim();
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        PlannedHours = plannedHours;
        ComputedAt = DateTime.UtcNow;
    }

    public DateOnly ProductionDate { get; private set; }

    public string ShiftCode { get; private set; } = string.Empty;

    public string ShiftName { get; private set; } = string.Empty;

    /// <summary>产线维度；空串表示全局。</summary>
    public string LineName { get; private set; } = string.Empty;

    public DateTime StartAtUtc { get; private set; }

    public DateTime EndAtUtc { get; private set; }

    /// <summary>计划生产时长（小时）。</summary>
    public double PlannedHours { get; private set; }

    // ---- 产量（SN 口径）----
    public int TotalSn { get; private set; }

    public int CompletedSn { get; private set; }

    public int ScrappedSn { get; private set; }

    public int InProcessSn { get; private set; }

    public int OnHoldSn { get; private set; }

    // ---- 质量（检验单口径）----
    public int InspectionTotal { get; private set; }

    public int InspectionPassed { get; private set; }

    public int InspectionFailed { get; private set; }

    public int InspectionConcessioned { get; private set; }

    public int DefectQuantity { get; private set; }

    // ---- 工时（工序口径，OEE 性能维度）----
    public long TheoreticalSeconds { get; private set; }

    public long ActualSeconds { get; private set; }

    /// <summary>停机时长（秒）与次数（OEE 可用率维度）。</summary>
    public long DowntimeSeconds { get; private set; }

    public int DowntimeCount { get; private set; }

    public DateTime ComputedAt { get; private set; }

    /// <summary>良率 = 完工 / (完工 + 报废)。</summary>
    public decimal YieldRate => CompletedSn + ScrappedSn == 0
        ? 0m
        : Math.Round((decimal)CompletedSn / (CompletedSn + ScrappedSn) * 100m, 2);

    /// <summary>一次合格率 = 合格 / 已判定（让步接收计为未一次合格）。</summary>
    public decimal Fpy
    {
        get
        {
            var judged = InspectionPassed + InspectionFailed + InspectionConcessioned;
            return judged == 0 ? 0m : Math.Round((decimal)InspectionPassed / judged * 100m, 2);
        }
    }

    /// <summary>用最新明细覆盖汇总值（重算幂等）。</summary>
    public void Update(
        string shiftName,
        DateTime startAtUtc,
        DateTime endAtUtc,
        double plannedHours,
        int totalSn,
        int completedSn,
        int scrappedSn,
        int inProcessSn,
        int onHoldSn,
        int inspectionTotal,
        int inspectionPassed,
        int inspectionFailed,
        int inspectionConcessioned,
        int defectQuantity,
        long theoreticalSeconds,
        long actualSeconds,
        long downtimeSeconds,
        int downtimeCount)
    {
        ShiftName = shiftName.Trim();
        StartAtUtc = startAtUtc;
        EndAtUtc = endAtUtc;
        PlannedHours = plannedHours;
        TotalSn = totalSn;
        CompletedSn = completedSn;
        ScrappedSn = scrappedSn;
        InProcessSn = inProcessSn;
        OnHoldSn = onHoldSn;
        InspectionTotal = inspectionTotal;
        InspectionPassed = inspectionPassed;
        InspectionFailed = inspectionFailed;
        InspectionConcessioned = inspectionConcessioned;
        DefectQuantity = defectQuantity;
        TheoreticalSeconds = theoreticalSeconds;
        ActualSeconds = actualSeconds;
        DowntimeSeconds = downtimeSeconds;
        DowntimeCount = downtimeCount;
        ComputedAt = DateTime.UtcNow;
    }
}
