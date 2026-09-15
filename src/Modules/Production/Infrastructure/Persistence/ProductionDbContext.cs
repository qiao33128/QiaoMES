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
    public DbSet<WorkOrderDailySequence> WorkOrderDailySequences => Set<WorkOrderDailySequence>();

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
        // 列表页默认按创建时间倒序 + 状态筛选，建立组合索引避免全表排序
        workOrder.HasIndex(w => new { w.Status, w.CreatedAt });
        workOrder.HasQueryFilter(w => !w.IsDeleted);
        workOrder.HasMany(w => w.Reports)
            .WithOne()
            .HasForeignKey(r => r.WorkOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        var report = modelBuilder.Entity<ProductionReport>();
        report.ToTable("production_reports");
        report.HasKey(r => r.Id);
        report.HasIndex(r => r.WorkOrderId);
        report.HasQueryFilter(r => !r.IsDeleted);

        var sequence = modelBuilder.Entity<WorkOrderDailySequence>();
        sequence.ToTable("work_order_daily_sequences");
        sequence.HasKey(s => s.SequenceDate);
        sequence.Property(s => s.SequenceDate).HasColumnName("sequence_date");
        sequence.Property(s => s.LastValue).HasColumnName("last_value").IsRequired();
        sequence.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        base.OnModelCreating(modelBuilder);
    }
}
