using Microsoft.EntityFrameworkCore;
using QiaoMES.Production.Domain;

namespace QiaoMES.Production.Infrastructure.Persistence;

/// <summary>
/// 生产模块数据库上下文。
/// </summary>
public class ProductionDbContext(DbContextOptions<ProductionDbContext> options) : DbContext(options)
{
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<ProductionReport> ProductionReports => Set<ProductionReport>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("production");

        var workOrder = modelBuilder.Entity<WorkOrder>();
        workOrder.ToTable("work_orders");
        workOrder.HasKey(w => w.Id);
        workOrder.Property(w => w.OrderNumber).HasMaxLength(50).IsRequired();
        workOrder.Property(w => w.ProductCode).HasMaxLength(100).IsRequired();
        workOrder.Property(w => w.ProductName).HasMaxLength(200).IsRequired();
        workOrder.Property(w => w.WorkCenter).HasMaxLength(100);
        workOrder.Property(w => w.Remark).HasMaxLength(500);
        workOrder.Property(w => w.Status).HasConversion<int>();
        workOrder.HasIndex(w => w.OrderNumber).IsUnique();
        workOrder.HasQueryFilter(w => !w.IsDeleted);
        workOrder.HasMany(w => w.Reports)
            .WithOne()
            .HasForeignKey(r => r.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        var report = modelBuilder.Entity<ProductionReport>();
        report.ToTable("production_reports");
        report.HasKey(r => r.Id);
        report.HasQueryFilter(r => !r.IsDeleted);

        base.OnModelCreating(modelBuilder);
    }
}
