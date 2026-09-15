using Microsoft.EntityFrameworkCore;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Infrastructure.Persistence;

public class MaterialLotRepository(QualityDbContext db) : IMaterialLotRepository
{
    public async Task<MaterialLot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.MaterialLots.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<MaterialLot?> GetByLotNumberAsync(string lotNumber, CancellationToken cancellationToken = default)
        => await db.MaterialLots.FirstOrDefaultAsync(l => l.LotNumber == lotNumber, cancellationToken);

    public async Task<bool> IsLotNumberTakenAsync(
        string lotNumber,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default)
        => await db.MaterialLots.AnyAsync(
            l => l.LotNumber == lotNumber && (excludeId == null || l.Id != excludeId),
            cancellationToken);

    public async Task<(IReadOnlyList<MaterialLot> Items, int TotalCount)> QueryAsync(
        MaterialLotQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<MaterialLot> source = db.MaterialLots.AsNoTracking();

        if (query.Status is not null)
        {
            source = source.Where(l => l.Status == query.Status);
        }
        if (!string.IsNullOrWhiteSpace(query.MaterialCode))
        {
            source = source.Where(l => l.MaterialCode == query.MaterialCode);
        }
        if (query.From is not null)
        {
            source = source.Where(l => l.ReceivedAt >= query.From);
        }
        if (query.To is not null)
        {
            source = source.Where(l => l.ReceivedAt <= query.To);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            var escape = LikePattern.EscapeCharacter;
            source = source.Where(l =>
                EF.Functions.ILike(l.LotNumber, pattern, escape) ||
                EF.Functions.ILike(l.MaterialCode, pattern, escape) ||
                (l.MaterialName != null && EF.Functions.ILike(l.MaterialName, pattern, escape)) ||
                (l.Supplier != null && EF.Functions.ILike(l.Supplier, pattern, escape)));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(l => l.ReceivedAt)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(int SnCount, decimal ConsumedQuantity)> GetLotConsumptionSummaryAsync(
        string lotNumber,
        CancellationToken cancellationToken = default)
    {
        var result = await db.SnMaterialConsumptions
            .AsNoTracking()
            .Where(c => c.LotNumber == lotNumber)
            .GroupBy(c => c.LotNumber)
            .Select(group => new
            {
                SnCount = group.Select(c => c.Sn).Distinct().Count(),
                Total = group.Sum(c => c.Quantity),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return result is null ? (0, 0m) : (result.SnCount, result.Total);
    }

    public async Task<IReadOnlyList<SnMaterialConsumption>> GetConsumptionsBySnAsync(
        string sn,
        CancellationToken cancellationToken = default)
        => await db.SnMaterialConsumptions
            .AsNoTracking()
            .Where(c => c.Sn == sn)
            .OrderBy(c => c.BoundAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SnMaterialConsumption>> GetConsumptionsByLotAsync(
        string lotNumber,
        int take,
        CancellationToken cancellationToken = default)
        => await db.SnMaterialConsumptions
            .AsNoTracking()
            .Where(c => c.LotNumber == lotNumber)
            .OrderByDescending(c => c.BoundAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<bool> ConsumptionExistsAsync(
        string sn,
        string lotNumber,
        string materialCode,
        CancellationToken cancellationToken = default)
        => await db.SnMaterialConsumptions.AnyAsync(
            c => c.Sn == sn && c.LotNumber == lotNumber && c.MaterialCode == materialCode,
            cancellationToken);

    public void Add(MaterialLot lot) => db.MaterialLots.Add(lot);

    public void AddConsumption(SnMaterialConsumption consumption) => db.SnMaterialConsumptions.Add(consumption);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
