using Microsoft.EntityFrameworkCore;

namespace QiaoMES.Infrastructure.Integrations;

public interface IApiClientRepository
{
    Task<ApiClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApiClient?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ApiClient> Items, int TotalCount)> QueryAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Guid?> ResolveByKeyAsync(string plainKey, CancellationToken cancellationToken = default);

    void Add(ApiClient client);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class ApiClientRepository(IntegrationDbContext db) : IApiClientRepository
{
    public async Task<ApiClient?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.ApiClients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<ApiClient?> GetByKeyHashAsync(string keyHash, CancellationToken cancellationToken = default)
        => await db.ApiClients.FirstOrDefaultAsync(c => c.ApiKeyHash == keyHash, cancellationToken);

    public async Task<(IReadOnlyList<ApiClient> Items, int TotalCount)> QueryAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var source = db.ApiClients.AsNoTracking();
        var totalCount = await source.CountAsync(cancellationToken);
        var normalizedPageSize = pageSize is < 1 or > 200 ? 20 : pageSize;

        var items = await source
            .OrderByDescending(c => c.CreatedAt)
            .Skip((Math.Max(page, 1) - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Guid?> ResolveByKeyAsync(string plainKey, CancellationToken cancellationToken = default)
    {
        var hash = ApiClient.ComputeHash(plainKey);
        var client = await db.ApiClients
            .AsNoTracking()
            .Where(c => c.ApiKeyHash == hash && c.IsActive)
            .Select(c => new { c.Id, c.ExpiresAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (client is null || (client.ExpiresAt is not null && client.ExpiresAt <= DateTime.UtcNow))
        {
            return null;
        }

        return client.Id;
    }

    public void Add(ApiClient client) => db.ApiClients.Add(client);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
