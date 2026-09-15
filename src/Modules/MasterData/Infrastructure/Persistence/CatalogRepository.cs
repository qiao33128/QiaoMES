using Microsoft.EntityFrameworkCore;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Infrastructure.Persistence;

public class CatalogRepository(MasterDataDbContext db) : ICatalogRepository
{
    public async Task<TEntity?> GetByIdAsync<TEntity>(Guid id, CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity
    {
        return await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    public async Task<TEntity?> GetByCodeAsync<TEntity>(string code, CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity
    {
        return await db.Set<TEntity>().FirstOrDefaultAsync(e => e.Code == code, cancellationToken);
    }

    public async Task<bool> IsCodeTakenAsync<TEntity>(
        string code,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity
    {
        return await db.Set<TEntity>()
            .AnyAsync(e => e.Code == code && (excludeId == null || e.Id != excludeId), cancellationToken);
    }

    public async Task<(IReadOnlyList<TEntity> Items, int TotalCount)> QueryPagedAsync<TEntity>(
        CatalogQuery query,
        CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity
    {
        IQueryable<TEntity> source = db.Set<TEntity>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            source = source.Where(e =>
                EF.Functions.ILike(e.Code, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(e.Name, pattern, LikePattern.EscapeCharacter));
        }

        if (query.IsActive is not null)
        {
            source = source.Where(e => e.IsActive == query.IsActive);
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(e => e.Code)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<TEntity>> GetByIdsAsync<TEntity>(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
        where TEntity : CatalogEntity
    {
        return await db.Set<TEntity>()
            .Where(e => ids.Contains(e.Id))
            .ToListAsync(cancellationToken);
    }

    public void Add<TEntity>(TEntity entity) where TEntity : CatalogEntity
        => db.Set<TEntity>().Add(entity);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
