using Microsoft.EntityFrameworkCore;
using QiaoMES.Production.Domain;

namespace QiaoMES.Production.Infrastructure.Persistence;

public class WorkOrderRepository(ProductionDbContext db) : IWorkOrderRepository
{
    public async Task<WorkOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders
            .Include(w => w.Reports)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
    }

    public async Task<WorkOrder?> GetByNumberAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders
            .Include(w => w.Reports)
            .FirstOrDefaultAsync(w => w.OrderNumber == orderNumber, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkOrder>> GetByStatusAsync(WorkOrderStatus status, CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders
            .Include(w => w.Reports)
            .Where(w => w.Status == status)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkOrder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders
            .Include(w => w.Reports)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsOrderNumberTakenAsync(string orderNumber, CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders.AnyAsync(w => w.OrderNumber == orderNumber, cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await db.WorkOrders.CountAsync(cancellationToken);
    }

    public void Add(WorkOrder workOrder) => db.WorkOrders.Add(workOrder);

    public void Update(WorkOrder workOrder) => db.WorkOrders.Update(workOrder);

    public void AddReport(ProductionReport report) => db.ProductionReports.Add(report);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
