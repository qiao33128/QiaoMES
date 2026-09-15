using Microsoft.EntityFrameworkCore;
using QiaoMES.Production.Domain;

namespace QiaoMES.Production.Infrastructure.Persistence;

public class WorkOrderRepository(ProductionDbContext db) : IWorkOrderRepository
{
    public async Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders
            .Include(w => w.Operations)
            .Include(w => w.Reports)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<WorkOrder?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders
            .Include(w => w.Operations)
            .Include(w => w.Reports)
            .FirstOrDefaultAsync(w => w.OrderNumber == orderNumber, cancellationToken);
    }

    public async Task<(IReadOnlyList<WorkOrder> Items, int TotalCount)> QueryAsync(
        WorkOrderQuery query,
        CancellationToken cancellationToken = default)
    {
        // 注意：这里构建的是 IQueryable，全部条件最终翻译成一条 SQL，
        // 不会把整表加载到内存。
        var workOrders = db.WorkOrders.AsNoTracking();

        if (query.Status is not null)
        {
            workOrders = workOrders.Where(w => w.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            // 转义 LIKE 通配符，避免用户输入 "%" 或 "_" 影响匹配结果
            var pattern = $"%{EscapeLikePattern(query.Keyword.Trim())}%";
            workOrders = workOrders.Where(w =>
                EF.Functions.ILike(w.OrderNumber, pattern, "\\") ||
                EF.Functions.ILike(w.ProductCode, pattern, "\\") ||
                EF.Functions.ILike(w.ProductName, pattern, "\\"));
        }

        var totalCount = await workOrders.CountAsync(cancellationToken);

        var items = await workOrders
            .OrderByDescending(w => w.CreatedAt)
            .ThenByDescending(w => w.OrderNumber)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public void Add(WorkOrder workOrder) => db.WorkOrders.Add(workOrder);

    public void AddOperation(WorkOrderOperation operation) => db.WorkOrderOperations.Add(operation);

    public void Update(WorkOrder workOrder) => db.WorkOrders.Update(workOrder);

    public void AddReport(ProductionReport report) => db.ProductionReports.Add(report);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    private static string EscapeLikePattern(string input)
        => input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
