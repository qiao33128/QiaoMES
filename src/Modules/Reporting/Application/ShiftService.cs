using QiaoMES.Reporting.Application.Contracts;
using QiaoMES.Reporting.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Reporting.Application;

/// <summary>
/// 班次与生产日历服务：提供全系统统一的「生产日 + 班次」时间口径。<para>
/// 报表统计必须先经本服务把自然日区间换算成班次窗口，否则跨天夜班会被算到错误的日期。
/// </para>
/// </summary>
public interface IShiftService
{
    Task<Result<PagedResult<ShiftDto>>> GetListAsync(ShiftQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> CreateAsync(CreateShiftRequest request, CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> UpdateAsync(Guid id, UpdateShiftRequest request, CancellationToken cancellationToken = default);

    Task<Result<ShiftDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>解析当前时刻所属班次（未配置班次时返回全天窗口）。</summary>
    Task<Result<CurrentShiftDto>> GetCurrentAsync(string? lineName = null, DateTime? at = null, CancellationToken cancellationToken = default);

    /// <summary>把生产日区间换算为班次时间窗集合（UTC 边界），供报表聚合使用。</summary>
    Task<Result<IReadOnlyList<ShiftRangeDto>>> GetRangesAsync(ShiftRangeRequest request, CancellationToken cancellationToken = default);

    // ---------------- 日历 ----------------

    Task<Result<PagedResult<CalendarDayDto>>> GetCalendarAsync(CalendarQueryRequest query, CancellationToken cancellationToken = default);

    /// <summary>新增或更新指定日期的日历（调休 / 节假日）。</summary>
    Task<Result<CalendarDayDto>> UpsertCalendarDayAsync(UpsertCalendarDayRequest request, CancellationToken cancellationToken = default);
}

public class ShiftService(
    IShiftRepository repository,
    ICalendarRepository calendarRepository) : IShiftService
{
    public async Task<Result<PagedResult<ShiftDto>>> GetListAsync(
        ShiftQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new ShiftQuery
        {
            IsActive = request.IsActive,
            LineName = request.LineName,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        return Result.Success(new PagedResult<ShiftDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<ShiftDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var shift = await repository.GetByIdAsync(id, cancellationToken);
        return shift is null ? NotFound() : Result.Success(ToDto(shift));
    }

    public async Task<Result<ShiftDto>> CreateAsync(
        CreateShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<ShiftDto>(Error.Validation("Shift.InvalidInput", "班次代码与名称不能为空"));
        }
        if (request.StartTime == request.EndTime)
        {
            return Result.Failure<ShiftDto>(Error.Validation("Shift.InvalidTime", "开始时间与结束时间不能相同"));
        }

        var code = request.Code.Trim();
        if (await repository.IsCodeTakenAsync(code, null, cancellationToken))
        {
            return Result.Failure<ShiftDto>(Error.Conflict("Shift.CodeTaken", $"班次代码 {code} 已存在"));
        }

        var shift = new ShiftDefinition(
            code,
            request.Name,
            request.StartTime,
            request.EndTime,
            request.LineName,
            request.Sequence,
            request.Remark);

        repository.Add(shift);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(shift));
    }

    public async Task<Result<ShiftDto>> UpdateAsync(
        Guid id,
        UpdateShiftRequest request,
        CancellationToken cancellationToken = default)
    {
        var shift = await repository.GetByIdAsync(id, cancellationToken);
        if (shift is null)
        {
            return NotFound();
        }
        if (request.StartTime == request.EndTime)
        {
            return Result.Failure<ShiftDto>(Error.Validation("Shift.InvalidTime", "开始时间与结束时间不能相同"));
        }

        shift.Update(request.Name, request.StartTime, request.EndTime, request.LineName, request.Sequence, request.Remark);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(shift));
    }

    public async Task<Result<ShiftDto>> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var shift = await repository.GetByIdAsync(id, cancellationToken);
        if (shift is null)
        {
            return NotFound();
        }

        shift.SetActive(isActive);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(shift));
    }

    public async Task<Result<CurrentShiftDto>> GetCurrentAsync(
        string? lineName = null,
        DateTime? at = null,
        CancellationToken cancellationToken = default)
    {
        var localNow = at ?? DateTime.Now;
        var shifts = await repository.GetActiveAsync(lineName, cancellationToken);

        foreach (var shift in shifts)
        {
            var window = shift.Resolve(localNow);
            if (window is not null)
            {
                return Result.Success(ToCurrentDto(window));
            }
        }

        // 未配置班次或当前处于班次间隙：回退为「全天」，保证报表口径不中断
        var date = DateOnly.FromDateTime(localNow);
        var start = date.ToDateTime(TimeOnly.MinValue);
        return Result.Success(new CurrentShiftDto(
            date,
            Guid.Empty,
            "FULLDAY",
            "全天（未配置班次或班次间隙）",
            lineName,
            start,
            start.AddDays(1),
            24));
    }

    public async Task<Result<IReadOnlyList<ShiftRangeDto>>> GetRangesAsync(
        ShiftRangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var end = request.To ?? DateOnly.FromDateTime(DateTime.Now);
        var start = request.From ?? end.AddDays(-6);
        if (start > end)
        {
            (start, end) = (end, start);
        }
        if (end.DayNumber - start.DayNumber > 366)
        {
            start = end.AddDays(-366);
        }

        var shifts = await repository.GetActiveAsync(request.LineName, cancellationToken);
        var calendar = await calendarRepository.GetRangeAsync(start, end, cancellationToken);
        var holidays = calendar.Where(c => !c.IsWorkingDay).Select(c => c.Date).ToHashSet();

        var ranges = new List<ShiftRangeDto>();

        for (var date = start; date <= end; date = date.AddDays(1))
        {
            // 节假日 / 停产日不计入计划生产时间
            if (holidays.Contains(date))
            {
                continue;
            }

            if (shifts.Count == 0)
            {
                // 无班次定义：退化为自然日全天，保证报表可用且口径明确
                var dayStart = date.ToDateTime(TimeOnly.MinValue);
                ranges.Add(new ShiftRangeDto(
                    date,
                    "FULLDAY",
                    "全天（未配置班次）",
                    request.LineName,
                    ToUtc(dayStart),
                    ToUtc(dayStart.AddDays(1)),
                    24));
                continue;
            }

            foreach (var shift in shifts)
            {
                var window = shift.ForProductionDate(date);
                ranges.Add(new ShiftRangeDto(
                    window.ProductionDate,
                    window.ShiftCode,
                    window.ShiftName,
                    window.LineName,
                    ToUtc(window.StartAt),
                    ToUtc(window.EndAt),
                    window.DurationHours));
            }
        }

        return Result.Success<IReadOnlyList<ShiftRangeDto>>(ranges);
    }

    public async Task<Result<PagedResult<CalendarDayDto>>> GetCalendarAsync(
        CalendarQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await calendarRepository.QueryAsync(
            request.From, request.To, request.Page, request.PageSize, cancellationToken);

        return Result.Success(new PagedResult<CalendarDayDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = request.Page < 1 ? 1 : request.Page,
            PageSize = request.PageSize is < 1 or > 400 ? 100 : request.PageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<CalendarDayDto>> UpsertCalendarDayAsync(
        UpsertCalendarDayRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await calendarRepository.GetByDateAsync(request.Date, cancellationToken);

        if (existing is null)
        {
            existing = new CalendarDay(request.Date, request.IsWorkingDay, request.Name, request.Remark);
            calendarRepository.Add(existing);
        }
        else
        {
            existing.Update(request.IsWorkingDay, request.Name, request.Remark);
        }

        await calendarRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(existing));
    }

    private static DateTime ToUtc(DateTime localTime)
        => DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime();

    private static Result<ShiftDto> NotFound()
        => Result.Failure<ShiftDto>(Error.NotFound("Shift.NotFound", "班次不存在"));

    private static ShiftDto ToDto(ShiftDefinition shift) => new(
        shift.Id,
        shift.Code,
        shift.Name,
        shift.StartTime,
        shift.EndTime,
        shift.LineName,
        shift.Sequence,
        shift.IsActive,
        shift.CrossesMidnight,
        shift.Remark,
        shift.CreatedAt,
        shift.UpdatedAt);

    private static CurrentShiftDto ToCurrentDto(Domain.ShiftWindow window) => new(
        window.ProductionDate,
        window.ShiftId,
        window.ShiftCode,
        window.ShiftName,
        window.LineName,
        window.StartAt,
        window.EndAt,
        window.DurationHours);

    private static CalendarDayDto ToDto(CalendarDay day) => new(
        day.Id,
        day.Date,
        day.IsWorkingDay,
        day.Name,
        day.Remark,
        day.CreatedAt,
        day.UpdatedAt);
}
