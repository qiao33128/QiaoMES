using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using QiaoMES.Equipment.Domain;
using QiaoMES.Shared;

// 设备实体名与模块根命名空间同名，用别名消除解析歧义
using EquipmentEntity = QiaoMES.Equipment.Domain.Equipment;

namespace QiaoMES.Equipment.Infrastructure.Persistence;

/// <summary>Andon 单号原子分配（与业务写入同事务）。</summary>
internal static class EquipmentNumberAllocator
{
    private const string Sql = """
        INSERT INTO equipment.equipment_number_sequences (sequence_key, last_value, updated_at)
        VALUES (@key, 1, now())
        ON CONFLICT (sequence_key)
        DO UPDATE SET last_value = equipment.equipment_number_sequences.last_value + 1,
                      updated_at = now()
        RETURNING last_value;
        """;

    public static async Task<int> NextAsync(EquipmentDbContext db, string key, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = Sql;
            command.CommandTimeout = 10;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            var parameter = command.CreateParameter();
            parameter.ParameterName = "key";
            parameter.Value = key;
            command.Parameters.Add(parameter);

            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(scalar, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}

public class EquipmentRepository(EquipmentDbContext db) : IEquipmentRepository
{
    public async Task<EquipmentEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.Equipments
            .Include(e => e.StatusLogs)
            .Include(e => e.MaintenanceRecords)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<EquipmentEntity?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => await db.Equipments.FirstOrDefaultAsync(e => e.Code == code, cancellationToken);

    public async Task<bool> IsCodeTakenAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
        => await db.Equipments.AnyAsync(
            e => e.Code == code && (excludeId == null || e.Id != excludeId),
            cancellationToken);

    public async Task<(IReadOnlyList<EquipmentEntity> Items, int TotalCount)> QueryAsync(
        EquipmentQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<EquipmentEntity> source = db.Equipments.AsNoTracking();

        if (query.Status is not null)
        {
            source = source.Where(e => e.Status == query.Status);
        }
        if (query.IsActive is not null)
        {
            source = source.Where(e => e.IsActive == query.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(query.LineName))
        {
            source = source.Where(e => e.LineName == query.LineName);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            var escape = LikePattern.EscapeCharacter;
            source = source.Where(e =>
                EF.Functions.ILike(e.Code, pattern, escape) ||
                EF.Functions.ILike(e.Name, pattern, escape) ||
                (e.Model != null && EF.Functions.ILike(e.Model, pattern, escape)));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(e => e.Code)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<(EquipmentStatus Status, int Count)>> CountByStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await db.Equipments
            .AsNoTracking()
            .Where(e => e.IsActive)
            .GroupBy(e => e.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return result.Select(x => (x.Status, x.Count)).ToList();
    }

    public async Task<IReadOnlyList<DowntimeParetoItem>> DowntimeParetoAsync(
        DateTime from,
        DateTime to,
        int top,
        CancellationToken cancellationToken = default)
    {
        // 统计「进入故障状态」的记录，按原因代码聚合（停机时长可从设备累计值进一步下钻）
        var result = await db.StatusLogs
            .AsNoTracking()
            .Where(log => log.ToStatus == EquipmentStatus.Down
                          && log.ChangedAt >= from
                          && log.ChangedAt <= to
                          && log.ReasonCode != null)
            .GroupBy(log => log.ReasonCode!)
            .Select(group => new { ReasonCode = group.Key, Count = group.Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .ToListAsync(cancellationToken);

        return result.Select(x => new DowntimeParetoItem(x.ReasonCode, x.Count)).ToList();
    }

    public void Add(EquipmentEntity equipment) => db.Equipments.Add(equipment);

    public void AddStatusLog(EquipmentStatusLog log) => db.StatusLogs.Add(log);

    public void AddMaintenanceRecord(EquipmentMaintenanceRecord record) => db.MaintenanceRecords.Add(record);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}

public class AndonRepository(EquipmentDbContext db) : IAndonRepository
{
    public async Task<AndonCall?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await db.AndonCalls.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<AndonCall> Items, int TotalCount)> QueryAsync(
        AndonQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AndonCall> source = db.AndonCalls.AsNoTracking();

        if (query.Status is not null)
        {
            source = source.Where(a => a.Status == query.Status);
        }
        if (query.Type is not null)
        {
            source = source.Where(a => a.Type == query.Type);
        }
        if (query.Level is not null)
        {
            source = source.Where(a => a.Level == query.Level);
        }
        if (query.OnlyOpen == true)
        {
            source = source.Where(a => a.Status == AndonStatus.Waiting || a.Status == AndonStatus.Responded);
        }
        if (query.From is not null)
        {
            source = source.Where(a => a.CalledAt >= query.From);
        }
        if (query.To is not null)
        {
            source = source.Where(a => a.CalledAt <= query.To);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = LikePattern.Contains(query.Keyword.Trim());
            var escape = LikePattern.EscapeCharacter;
            source = source.Where(a =>
                EF.Functions.ILike(a.CallNumber, pattern, escape) ||
                EF.Functions.ILike(a.Description, pattern, escape) ||
                (a.EquipmentCode != null && EF.Functions.ILike(a.EquipmentCode, pattern, escape)));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(a => a.CalledAt)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<AndonCall>> GetTimeoutCallsAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        return await db.AndonCalls
            .Where(a => a.Status == AndonStatus.Waiting
                        && !a.Escalated
                        && a.CalledAt.AddMinutes(a.TimeoutMinutes) < now)
            .OrderBy(a => a.CalledAt)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<string> NextNumberAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var key = $"ANDON-{date:yyyyMMdd}";
        var next = await EquipmentNumberAllocator.NextAsync(db, key, cancellationToken);
        return $"{key}-{next:D4}";
    }

    public void Add(AndonCall call) => db.AndonCalls.Add(call);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
