using Microsoft.EntityFrameworkCore;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Infrastructure.Persistence;

public class DefectCodeRepository(QualityDbContext db) : IDefectCodeRepository
{
    public async Task<DefectCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.DefectCodes.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<DefectCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await db.DefectCodes.FirstOrDefaultAsync(d => d.Code == code, cancellationToken);

    public async Task<bool> IsCodeTakenAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await db.DefectCodes.AnyAsync(
            d => d.Code == code && (excludeId == null || d.Id != excludeId),
            cancellationToken);

    public async Task<(IReadOnlyList<DefectCode> Items, int TotalCount)> QueryAsync(
        DefectCodeQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<DefectCode> source = db.DefectCodes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            source = source.Where(d => d.Category == query.Category);
        }
        if (query.IsActive is not null)
        {
            source = source.Where(d => d.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            var escape = LikePattern.EscapeCharacter;
            source = source.Where(d =>
                EF.Functions.ILike(d.Code, pattern, escape) ||
                EF.Functions.ILike(d.Name, pattern, escape));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(d => d.Code)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<(string DefectCode, int Count)>> TopDefectsAsync(
        DateTime from,
        DateTime to,
        int top,
        CancellationToken cancellationToken = default)
    {
        var inspectionIds = db.Inspections
            .Where(i => i.CreatedAt >= from && i.CreatedAt <= to)
            .Select(i => i.Id);

        var result = await db.InspectionItems
            .AsNoTracking()
            .Where(item => item.IsQualified == false
                           && item.DefectCode != null
                           && inspectionIds.Contains(item.InspectionId))
            .GroupBy(item => item.DefectCode!)
            .Select(group => new { DefectCode = group.Key, Count = group.Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(cancellationToken);

        return result.Select(x => (x.DefectCode, x.Count)).ToList();
    }

    public void Add(DefectCode defectCode) => db.DefectCodes.Add(defectCode);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
