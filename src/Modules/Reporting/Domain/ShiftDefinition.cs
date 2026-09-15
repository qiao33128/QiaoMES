using QiaoMES.Shared;

namespace QiaoMES.Reporting.Domain;

/// <summary>
/// 班次定义。<para>
/// 支持跨天班次（如 20:30 ~ 次日 08:30）：此时 <see cref="ProductionDate"/> 归属班次开始的那一天，
/// 所有统计口径必须按「生产日 + 班次」而非自然日，否则跨天班次会被算错。
/// </para>
/// </summary>
public class ShiftDefinition : Entity
{
    private ShiftDefinition() { }

    public ShiftDefinition(
        string code,
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        string? lineName = null,
        int sequence = 0,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        Code = code.Trim();
        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
        LineName = lineName?.Trim();
        Sequence = sequence;
        Remark = remark?.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    /// <summary>所属产线；为空表示全局默认班次。</summary>
    public string? LineName { get; private set; }

    /// <summary>排序（同一产线内）。</summary>
    public int Sequence { get; private set; }

    public bool IsActive { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    /// <summary>是否跨天（结束时间早于或等于开始时间）。</summary>
    public bool CrossesMidnight => EndTime <= StartTime;

    /// <summary>
    /// 判断某本地时刻落在哪个班次窗口内；不在任何班次内返回 <c>null</c>。
    /// </summary>
    public ShiftWindow? Resolve(DateTime localNow)
    {
        if (!IsActive)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(localNow);
        var time = TimeOnly.FromDateTime(localNow);

        if (!CrossesMidnight)
        {
            return time >= StartTime && time < EndTime
                ? Build(today, today.ToDateTime(StartTime), today.ToDateTime(EndTime))
                : null;
        }

        // 跨天班次：当天开始时间之后 → 生产日 = 今天
        if (time >= StartTime)
        {
            return Build(
                today,
                today.ToDateTime(StartTime),
                today.AddDays(1).ToDateTime(EndTime));
        }

        // 次日结束时间之前 → 生产日 = 昨天
        if (time < EndTime)
        {
            var productionDate = today.AddDays(-1);
            return Build(
                productionDate,
                productionDate.ToDateTime(StartTime),
                today.ToDateTime(EndTime));
        }

        return null;
    }

    /// <summary>取指定「生产日」的班次时间窗（不校验当前时刻是否落入）。</summary>
    public ShiftWindow ForProductionDate(DateOnly productionDate)
        => Build(
            productionDate,
            productionDate.ToDateTime(StartTime),
            productionDate.AddDays(CrossesMidnight ? 1 : 0).ToDateTime(EndTime));

    public void Update(
        string name,
        TimeOnly startTime,
        TimeOnly endTime,
        string? lineName,
        int sequence,
        string? remark)
    {
        Name = name.Trim();
        StartTime = startTime;
        EndTime = endTime;
        LineName = lineName?.Trim();
        Sequence = sequence;
        Remark = remark?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }

    private ShiftWindow Build(DateOnly productionDate, DateTime startAt, DateTime endAt)
        => new(productionDate, Id, Code, Name, LineName, startAt, endAt);
}

/// <summary>某个具体班次实例的时间窗（本地时间）。</summary>
public sealed record ShiftWindow(
    DateOnly ProductionDate,
    Guid ShiftId,
    string ShiftCode,
    string ShiftName,
    string? LineName,
    DateTime StartAt,
    DateTime EndAt)
{
    /// <summary>班次时长（小时）。</summary>
    public double DurationHours => (EndAt - StartAt).TotalHours;
}

/// <summary>
/// 生产日历：工作日 / 节假日 / 特殊生产日（调休）。
/// </summary>
public class CalendarDay : Entity
{
    private CalendarDay() { }

    public CalendarDay(DateOnly date, bool isWorkingDay, string? name = null, string? remark = null)
        : base(Guid.NewGuid())
    {
        Date = date;
        IsWorkingDay = isWorkingDay;
        Name = name?.Trim();
        Remark = remark?.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    public DateOnly Date { get; private set; }

    /// <summary>是否生产日（false 表示节假日 / 停产）。</summary>
    public bool IsWorkingDay { get; private set; }

    /// <summary>节假日名称，如「国庆节」。</summary>
    public string? Name { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public void Update(bool isWorkingDay, string? name, string? remark)
    {
        IsWorkingDay = isWorkingDay;
        Name = name?.Trim();
        Remark = remark?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
