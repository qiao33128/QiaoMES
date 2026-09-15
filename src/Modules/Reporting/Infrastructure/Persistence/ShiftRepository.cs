using Microsoft.EntityFrameworkCore;
using QiaoMES.Reporting.Domain;

namespace QiaoMES.Reporting.Infrastructure.Persistence;

public class ShiftRepository(ReportingDbContext db) : IShiftRepository
{
    public async Task<ShiftDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.Shifts.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<ShiftDefinition?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await db.Shifts.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);

    public async Task<bool> IsCodeTakenAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await db.Shifts.AnyAsync(
            s => s.Code == code && (excludeId == null || s.Id != excludeId),
            cancellationToken);

    public async Task<IReadOnlyList<ShiftDefinition>> GetActiveAsync(
        string? lineName,
        CancellationToken cancellationToken = default)
    {
        var all = await db.Shifts
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Sequence)
            .ThenBy(s => s.StartTime)
            .ToListAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(lineName))
        {
            // 未指定产线：优先全局班次，无全局班次时返回全部
            var global = all.Where(s => string.IsNullOrWhiteSpace(s.LineName)).ToList();
            return global.Count > 0 ? global : all;
        }

        // 指定产线：该产线班次优先，其次全局班次
        var scoped = all.Where(s => s.LineName == lineName).ToList();
        return scoped.Count > 0
            ? scoped
            : all.Where(s => string.IsNullOrWhiteSpace(s.LineName)).ToList();
    }

    public async Task<(IReadOnlyList<ShiftDefinition> Items, int TotalCount)> QueryAsync(
        ShiftQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ShiftDefinition> source = db.Shifts.AsNoTracking();

        if (query.IsActive is not null)
        {
            source = source.Where(s => s.IsActive == query.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(query.LineName))
        {
            source = source.Where(s => s.LineName == query.LineName);
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(s => s.Sequence)
            .ThenBy(s => s.StartTime)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public void Add(ShiftDefinition shift) => db.Shifts.Add(shift);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}

public class CalendarRepository(ReportingDbContext db) : ICalendarRepository
{
    public async Task<CalendarDay?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
        => await db.CalendarDays.FirstOrDefaultAsync(c => c.Date == date, cancellationToken);

    public async Task<IReadOnlyList<CalendarDay>> GetRangeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => await db.CalendarDays
            .AsNoTracking()
            .Where(c => c.Date >= from && c.Date <= to)
            .OrderBy(c => c.Date)
            .ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<CalendarDay> Items, int TotalCount)> QueryAsync(
        DateOnly? from,
        DateOnly? to,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CalendarDay> source = db.CalendarDays.AsNoTracking();

        if (from is not null)
        {
            source = source.Where(c => c.Date >= from);
        }
        if (to is not null)
        {
            source = source.Where(c => c.Date <= to);
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var normalizedPageSize = pageSize is < 1 or > 400 ? 100 : pageSize;
        var skip = (Math.Max(page, 1) - 1) * normalizedPageSize;

        var items = await source
            .OrderByDescending(c => c.Date)
            .Skip(skip)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public void Add(CalendarDay day) => db.CalendarDays.Add(day);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
