using Microsoft.EntityFrameworkCore;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Infrastructure.Persistence;

public class BomRepository(MasterDataDbContext db) : IBomRepository
{
    public async Task<Bom?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.Boms
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    public async Task<Bom?> GetActiveByProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await db.Boms
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.ProductId == productId && b.IsActive, cancellationToken);
    }

    public async Task<(IReadOnlyList<Bom> Items, int TotalCount)> QueryAsync(
        BomQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Bom> source = db.Boms.AsNoTracking().Include(b => b.Items);

        if (query.ProductId is not null)
        {
            source = source.Where(b => b.ProductId == query.ProductId);
        }

        if (query.IsActive is not null)
        {
            source = source.Where(b => b.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            source = source.Where(b => EF.Functions.ILike(b.Version, pattern, LikePattern.EscapeCharacter));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(b => b.CreatedAt)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> IsVersionTakenAsync(
        Guid productId,
        string version,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        return await db.Boms.AnyAsync(
            b => b.ProductId == productId && b.Version == version && (excludeId == null || b.Id != excludeId),
            cancellationToken);
    }

    public async Task DeactivateOtherVersionsAsync(Guid productId, Guid keepId, CancellationToken cancellationToken = default)
    {
        var others = await db.Boms
            .Where(b => b.ProductId == productId && b.Id != keepId && b.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var bom in others)
        {
            bom.Deactivate();
        }
    }

    public void Add(Bom bom) => db.Boms.Add(bom);

    public void AddItem(BomItem item) => db.BomItems.Add(item);

    public void Remove(Bom bom) => db.Boms.Remove(bom);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
