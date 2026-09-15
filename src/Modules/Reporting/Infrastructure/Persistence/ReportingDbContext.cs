using Microsoft.EntityFrameworkCore;
using QiaoMES.Reporting.Domain;
using QiaoMES.Reporting.Infrastructure.Persistence.ReadModels;

namespace QiaoMES.Reporting.Infrastructure.Persistence;

/// <summary>
/// 报表模块数据库上下文（schema：<c>reporting</c>）：班次定义、生产日历，以及其它模块的只读投影。
/// </summary>
public class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<ShiftDefinition> Shifts => Set<ShiftDefinition>();
    public DbSet<CalendarDay> CalendarDays => Set<CalendarDay>();

    // ---- 只读投影：映射其它模块的表做 SQL 级聚合，不参与本模块迁移 ----
    public DbSet<WorkOrderReadModel> WorkOrders => Set<WorkOrderReadModel>();
    public DbSet<WorkOrderOperationReadModel> WorkOrderOperations => Set<WorkOrderOperationReadModel>();
    public DbSet<SerialNumberReadModel> SerialNumbers => Set<SerialNumberReadModel>();
    public DbSet<EquipmentStatusLogReadModel> EquipmentStatusLogs => Set<EquipmentStatusLogReadModel>();
    public DbSet<InspectionReadModel> Inspections => Set<InspectionReadModel>();
    public DbSet<InspectionItemReadModel> InspectionItems => Set<InspectionItemReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("reporting");

        var shift = modelBuilder.Entity<ShiftDefinition>();
        shift.ToTable("shifts");
        shift.HasKey(s => s.Id);
        shift.Property(s => s.Code).HasMaxLength(50).IsRequired();
        shift.Property(s => s.Name).HasMaxLength(100).IsRequired();
        shift.Property(s => s.LineName).HasMaxLength(100);
        shift.Property(s => s.Remark).HasMaxLength(500);
        shift.HasIndex(s => s.Code).IsUnique();
        shift.HasIndex(s => new { s.LineName, s.IsActive });
        shift.HasQueryFilter(s => !s.IsDeleted);

        var calendar = modelBuilder.Entity<CalendarDay>();
        calendar.ToTable("calendar_days");
        calendar.HasKey(c => c.Id);
        calendar.Property(c => c.Name).HasMaxLength(100);
        calendar.Property(c => c.Remark).HasMaxLength(500);
        calendar.HasIndex(c => c.Date).IsUnique();
        calendar.HasQueryFilter(c => !c.IsDeleted);

        // ---- 只读投影映射：表由各业务模块的迁移维护，本模块不建表 ----
        modelBuilder.Entity<WorkOrderReadModel>(entity =>
        {
            entity.ToTable("work_orders", "production", table => table.ExcludeFromMigrations());
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<WorkOrderOperationReadModel>(entity =>
        {
            entity.ToTable("work_order_operations", "production", table => table.ExcludeFromMigrations());
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.WorkOrderId);
        });

        modelBuilder.Entity<SerialNumberReadModel>(entity =>
        {
            entity.ToTable("serial_numbers", "production", table => table.ExcludeFromMigrations());
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<EquipmentStatusLogReadModel>(entity =>
        {
            entity.ToTable("equipment_status_logs", "equipment", table => table.ExcludeFromMigrations());
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ChangedAt);
        });

        modelBuilder.Entity<InspectionReadModel>(entity =>
        {
            entity.ToTable("inspections", "quality", table => table.ExcludeFromMigrations());
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<InspectionItemReadModel>(entity =>
        {
            entity.ToTable("inspection_items", "quality", table => table.ExcludeFromMigrations());
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.InspectionId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
