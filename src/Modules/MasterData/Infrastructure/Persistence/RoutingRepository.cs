using Microsoft.EntityFrameworkCore;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Infrastructure.Persistence;

public class RoutingRepository(MasterDataDbContext db) : IRoutingRepository
{
    public async Task<Routing?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.Routings
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Routing?> GetActiveByProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        return await db.Routings
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.IsActive, cancellationToken);
    }

    public async Task<(IReadOnlyList<Routing> Items, int TotalCount)> QueryAsync(
        RoutingQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Routing> source = db.Routings.AsNoTracking().Include(r => r.Steps);

        if (query.ProductId is not null)
        {
            source = source.Where(r => r.ProductId == query.ProductId);
        }

        if (query.IsActive is not null)
        {
            source = source.Where(r => r.IsActive == query.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            source = source.Where(r => EF.Functions.ILike(r.Version, pattern, LikePattern.EscapeCharacter));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(r => r.CreatedAt)
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
        return await db.Routings.AnyAsync(
            r => r.ProductId == productId && r.Version == version && (excludeId == null || r.Id != excludeId),
            cancellationToken);
    }

    public async Task DeactivateOtherVersionsAsync(Guid productId, Guid keepId, CancellationToken cancellationToken = default)
    {
        var others = await db.Routings
            .Where(r => r.ProductId == productId && r.Id != keepId && r.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var routing in others)
        {
            routing.Deactivate();
        }
    }

    public void Add(Routing routing) => db.Routings.Add(routing);

    public void AddStep(RoutingStep step) => db.RoutingSteps.Add(step);

    public void Remove(Routing routing) => db.Routings.Remove(routing);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
