using Microsoft.EntityFrameworkCore;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Infrastructure.Persistence;

public class SerialNumberRepository(ProductionDbContext db) : ISerialNumberRepository
{
    public async Task<SerialNumber?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.SerialNumbers
            .Include(s => s.Trackings)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<SerialNumber?> GetBySnAsync(string sn, CancellationToken cancellationToken = default)
    {
        return await db.SerialNumbers
            .Include(s => s.Trackings)
            .FirstOrDefaultAsync(s => s.Sn == sn, cancellationToken);
    }

    public async Task<bool> ExistsAsync(string sn, CancellationToken cancellationToken = default)
        => await db.SerialNumbers.AnyAsync(s => s.Sn == sn, cancellationToken);

    public async Task<int> CountByWorkOrderAsync(Guid workOrderId, CancellationToken cancellationToken = default)
        => await db.SerialNumbers.CountAsync(s => s.WorkOrderId == workOrderId, cancellationToken);

    public async Task<(IReadOnlyList<SerialNumber> Items, int TotalCount)> QueryAsync(
        SerialNumberQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SerialNumber> source = db.SerialNumbers.AsNoTracking();

        if (query.WorkOrderId is not null)
        {
            source = source.Where(s => s.WorkOrderId == query.WorkOrderId);
        }

        if (query.Status is not null)
        {
            source = source.Where(s => s.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            source = source.Where(s => EF.Functions.ILike(s.Sn, pattern, LikePattern.EscapeCharacter));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(s => s.CreatedAt)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<(SerialNumberStatus Status, int Count)>> CountByStatusAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SerialNumber> source = db.SerialNumbers.AsNoTracking();

        if (from is not null)
        {
            source = source.Where(s => s.CreatedAt >= from);
        }
        if (to is not null)
        {
            source = source.Where(s => s.CreatedAt <= to);
        }

        var grouped = await source
            .GroupBy(s => s.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return grouped.Select(x => (x.Status, x.Count)).ToList();
    }

    public void Add(SerialNumber serialNumber) => db.SerialNumbers.Add(serialNumber);

    public void AddTracking(WipTracking tracking) => db.WipTrackings.Add(tracking);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
