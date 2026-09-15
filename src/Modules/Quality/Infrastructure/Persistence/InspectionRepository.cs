using Microsoft.EntityFrameworkCore;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Infrastructure.Persistence;

public class InspectionRepository(QualityDbContext db) : IInspectionRepository
{
    public async Task<Inspection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.Inspections
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<Inspection?> GetByNumberAsync(string inspectionNumber, CancellationToken cancellationToken = default)
        => await db.Inspections
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.InspectionNumber == inspectionNumber, cancellationToken);

    public async Task<(IReadOnlyList<Inspection> Items, int TotalCount)> QueryAsync(
        InspectionQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Inspection> source = db.Inspections.AsNoTracking().Include(i => i.Items);

        if (query.Type is not null)
        {
            source = source.Where(i => i.Type == query.Type);
        }
        if (query.Status is not null)
        {
            source = source.Where(i => i.Status == query.Status);
        }
        if (query.WorkOrderId is not null)
        {
            source = source.Where(i => i.WorkOrderId == query.WorkOrderId);
        }
        if (query.From is not null)
        {
            source = source.Where(i => i.CreatedAt >= query.From);
        }
        if (query.To is not null)
        {
            source = source.Where(i => i.CreatedAt <= query.To);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            var escape = LikePattern.EscapeCharacter;
            source = source.Where(i =>
                EF.Functions.ILike(i.InspectionNumber, pattern, escape) ||
                (i.Sn != null && EF.Functions.ILike(i.Sn, pattern, escape)) ||
                (i.ProductCode != null && EF.Functions.ILike(i.ProductCode, pattern, escape)) ||
                (i.MaterialCode != null && EF.Functions.ILike(i.MaterialCode, pattern, escape)));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(i => i.CreatedAt)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<string> NextNumberAsync(InspectionType type, DateTime date, CancellationToken cancellationToken = default)
    {
        var key = $"{type.ToString().ToUpperInvariant()}-{date:yyyyMMdd}";
        var next = await QualityNumberAllocator.NextAsync(db, key, cancellationToken);
        return $"{key}-{next:D4}";
    }

    public async Task<IReadOnlyList<Inspection>> GetBySnAsync(string sn, CancellationToken cancellationToken = default)
        => await db.Inspections
            .AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.Sn == sn)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SpcSample>> GetItemHistoryAsync(
        string itemName,
        DateTime? from,
        DateTime? to,
        int points,
        CancellationToken cancellationToken = default)
    {
        var samples = await db.InspectionItems
            .AsNoTracking()
            .Where(item => item.Name == itemName && item.NumericValue != null)
            .Join(
                db.Inspections.AsNoTracking(),
                item => item.InspectionId,
                inspection => inspection.Id,
                (item, inspection) => new { item, inspection })
            .Where(x => (from == null || x.inspection.CreatedAt >= from)
                        && (to == null || x.inspection.CreatedAt <= to))
            .OrderByDescending(x => x.inspection.CreatedAt)
            .Take(points)
            .Select(x => new SpcSample(
                x.inspection.CreatedAt,
                x.item.NumericValue,
                x.item.IsQualified,
                x.inspection.InspectionNumber))
            .ToListAsync(cancellationToken);

        // 取最近 N 条后按时间正序返回，便于前端画趋势
        return samples.OrderBy(s => s.Timestamp).ToList();
    }

    public void Add(Inspection inspection) => db.Inspections.Add(inspection);

    public void AddItem(InspectionItem item) => db.InspectionItems.Add(item);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
