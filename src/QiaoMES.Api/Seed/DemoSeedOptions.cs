namespace QiaoMES.Api.Seed;

/// <summary>
/// 演示数据生成参数。
/// <para>规模刻意保持"小":目标是让每个页面、每张报表都有数据可看,而不是压测。</para>
/// </summary>
public sealed record DemoSeedOptions(
    /// <summary>数据铺开的天数(越接近报表/SPC 的默认查询窗口越好)。</summary>
    int Days = 30,
    /// <summary>生成的工单数量(状态按草稿/已下达/生产中/已完工铺开)。</summary>
    int WorkOrders = 8,
    /// <summary>每张已下达工单生成的 SN 数量。</summary>
    int SnPerOrder = 50);

/// <summary>演示数据生成结果,用于前端提示与排错。</summary>
public sealed record DemoSeedSummary(
    int RemovedRows,
    int Shifts,
    int CalendarDays,
    int WorkCenters,
    int Products,
    int Materials,
    int Operations,
    int Boms,
    int Routings,
    int DefectCodes,
    int Equipments,
    int MaterialLots,
    int WorkOrders,
    int WorkOrderOperations,
    int SerialNumbers,
    int WipTrackings,
    int ProductionReports,
    int Inspections,
    int Nonconformances,
    int AndonCalls,
    int MetricShifts,
    string Note)
{
    public static DemoSeedSummary Empty(string note)
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, note);
}
