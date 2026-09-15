using Microsoft.EntityFrameworkCore;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Infrastructure.Persistence;

public class NonconformanceRepository(QualityDbContext db) : INonconformanceRepository
{
    public async Task<Nonconformance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.Nonconformances
            .Include(n => n.Repairs)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<Nonconformance> Items, int TotalCount)> QueryAsync(
        NonconformanceQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Nonconformance> source = db.Nonconformances.AsNoTracking().Include(n => n.Repairs);

        if (query.Status is not null)
        {
            source = source.Where(n => n.Status == query.Status);
        }
        if (query.Disposition is not null)
        {
            source = source.Where(n => n.Disposition == query.Disposition);
        }
        if (query.WorkOrderId is not null)
        {
            source = source.Where(n => n.WorkOrderId == query.WorkOrderId);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            var escape = LikePattern.EscapeCharacter;
            source = source.Where(n =>
                EF.Functions.ILike(n.NonconformanceNumber, pattern, escape) ||
                (n.Sn != null && EF.Functions.ILike(n.Sn, pattern, escape)) ||
                (n.DefectCode != null && EF.Functions.ILike(n.DefectCode, pattern, escape)) ||
                (n.ProductCode != null && EF.Functions.ILike(n.ProductCode, pattern, escape)));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(n => n.CreatedAt)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<string> NextNumberAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var key = $"NC-{date:yyyyMMdd}";
        var next = await QualityNumberAllocator.NextAsync(db, key, cancellationToken);
        return $"{key}-{next:D4}";
    }

    public async Task<int> CountByInspectionAsync(Guid inspectionId, CancellationToken cancellationToken = default)
        => await db.Nonconformances.CountAsync(n => n.InspectionId == inspectionId, cancellationToken);

    public void Add(Nonconformance nonconformance) => db.Nonconformances.Add(nonconformance);

    public void AddRepair(RepairRecord repair) => db.RepairRecords.Add(repair);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
