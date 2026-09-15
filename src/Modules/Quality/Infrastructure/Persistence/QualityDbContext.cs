using Microsoft.EntityFrameworkCore;
using QiaoMES.Quality.Domain;

namespace QiaoMES.Quality.Infrastructure.Persistence;

/// <summary>
/// 质量模块数据库上下文（schema：<c>quality</c>）。
/// </summary>
public class QualityDbContext(DbContextOptions<QualityDbContext> options) : DbContext(options)
{
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<InspectionItem> InspectionItems => Set<InspectionItem>();
    public DbSet<Nonconformance> Nonconformances => Set<Nonconformance>();
    public DbSet<RepairRecord> RepairRecords => Set<RepairRecord>();
    public DbSet<DefectCode> DefectCodes => Set<DefectCode>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<SnMaterialConsumption> SnMaterialConsumptions => Set<SnMaterialConsumption>();
    public DbSet<QualityDailySequence> DailySequences => Set<QualityDailySequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("quality");

        // ---------- 检验单 ----------
        var inspection = modelBuilder.Entity<Inspection>();
        inspection.ToTable("inspections");
        inspection.HasKey(i => i.Id);
        inspection.Property(i => i.InspectionNumber).HasMaxLength(50).IsRequired();
        inspection.Property(i => i.Sn).HasMaxLength(100);
        inspection.Property(i => i.MaterialCode).HasMaxLength(100);
        inspection.Property(i => i.LotNumber).HasMaxLength(100);
        inspection.Property(i => i.ProductCode).HasMaxLength(100);
        inspection.Property(i => i.AqlLevel).HasMaxLength(50);
        inspection.Property(i => i.InspectorName).HasMaxLength(100);
        inspection.Property(i => i.Remark).HasMaxLength(500);
        inspection.Property(i => i.Type).HasConversion<int>();
        inspection.Property(i => i.Status).HasConversion<int>();
        inspection.Property(i => i.Conclusion).HasConversion<int>();
        inspection.HasIndex(i => i.InspectionNumber).IsUnique();
        inspection.HasIndex(i => new { i.Type, i.Status });
        inspection.HasIndex(i => i.Sn);
        inspection.HasIndex(i => i.WorkOrderId);
        inspection.HasIndex(i => i.CreatedAt);
        inspection.HasQueryFilter(i => !i.IsDeleted);
        inspection.HasMany(i => i.Items)
            .WithOne()
            .HasForeignKey(item => item.InspectionId)
            .OnDelete(DeleteBehavior.Cascade);

        var item = modelBuilder.Entity<InspectionItem>();
        item.ToTable("inspection_items");
        item.HasKey(i => i.Id);
        item.Property(i => i.Name).HasMaxLength(200).IsRequired();
        item.Property(i => i.Standard).HasMaxLength(200);
        item.Property(i => i.MeasuredValue).HasMaxLength(200);
        item.Property(i => i.DefectCode).HasMaxLength(50);
        item.Property(i => i.Remark).HasMaxLength(500);
        item.Property(i => i.LowerLimit).HasPrecision(18, 4);
        item.Property(i => i.UpperLimit).HasPrecision(18, 4);
        item.Property(i => i.NumericValue).HasPrecision(18, 4);
        item.HasIndex(i => i.InspectionId);

        // ---------- 不合格处置 ----------
        var nonconformance = modelBuilder.Entity<Nonconformance>();
        nonconformance.ToTable("nonconformances");
        nonconformance.HasKey(n => n.Id);
        nonconformance.Property(n => n.NonconformanceNumber).HasMaxLength(50).IsRequired();
        nonconformance.Property(n => n.Sn).HasMaxLength(100);
        nonconformance.Property(n => n.ProductCode).HasMaxLength(100);
        nonconformance.Property(n => n.DefectCode).HasMaxLength(50);
        nonconformance.Property(n => n.DefectDescription).HasMaxLength(500);
        nonconformance.Property(n => n.Remark).HasMaxLength(500);
        nonconformance.Property(n => n.Disposition).HasConversion<int>();
        nonconformance.Property(n => n.Status).HasConversion<int>();
        nonconformance.HasIndex(n => n.NonconformanceNumber).IsUnique();
        nonconformance.HasIndex(n => n.Status);
        nonconformance.HasIndex(n => n.Sn);
        nonconformance.HasIndex(n => n.InspectionId);
        nonconformance.HasQueryFilter(n => !n.IsDeleted);
        nonconformance.HasMany(n => n.Repairs)
            .WithOne()
            .HasForeignKey(r => r.NonconformanceId)
            .OnDelete(DeleteBehavior.Cascade);

        var repair = modelBuilder.Entity<RepairRecord>();
        repair.ToTable("repair_records");
        repair.HasKey(r => r.Id);
        repair.Property(r => r.Description).HasMaxLength(500).IsRequired();
        repair.Property(r => r.Result).HasMaxLength(500);
        repair.Property(r => r.Remark).HasMaxLength(500);
        repair.HasIndex(r => r.NonconformanceId);

        // ---------- 不良代码 ----------
        var defectCode = modelBuilder.Entity<DefectCode>();
        defectCode.ToTable("defect_codes");
        defectCode.HasKey(d => d.Id);
        defectCode.Property(d => d.Code).HasMaxLength(50).IsRequired();
        defectCode.Property(d => d.Name).HasMaxLength(200).IsRequired();
        defectCode.Property(d => d.Category).HasMaxLength(50);
        defectCode.Property(d => d.Description).HasMaxLength(500);
        defectCode.HasIndex(d => d.Code).IsUnique();
        defectCode.HasIndex(d => new { d.Category, d.IsActive });
        defectCode.HasQueryFilter(d => !d.IsDeleted);

        // ---------- 单号日序列 ----------
        var sequence = modelBuilder.Entity<QualityDailySequence>();
        sequence.ToTable("quality_number_sequences");
        sequence.HasKey(s => s.SequenceKey);
        sequence.Property(s => s.SequenceKey).HasColumnName("sequence_key").HasMaxLength(50);
        sequence.Property(s => s.LastValue).HasColumnName("last_value").IsRequired();
        sequence.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // ---------- 来料批次（上游谱系） ----------
        var materialLot = modelBuilder.Entity<MaterialLot>();
        materialLot.ToTable("material_lots");
        materialLot.HasKey(l => l.Id);
        materialLot.Property(l => l.LotNumber).HasMaxLength(100).IsRequired();
        materialLot.Property(l => l.MaterialCode).HasMaxLength(100).IsRequired();
        materialLot.Property(l => l.MaterialName).HasMaxLength(200);
        materialLot.Property(l => l.Supplier).HasMaxLength(200);
        materialLot.Property(l => l.SupplierLotNumber).HasMaxLength(100);
        materialLot.Property(l => l.Unit).HasMaxLength(20);
        materialLot.Property(l => l.Quantity).HasPrecision(18, 4);
        materialLot.Property(l => l.RemainingQuantity).HasPrecision(18, 4);
        materialLot.Property(l => l.Status).HasConversion<int>();
        materialLot.Property(l => l.StatusReason).HasMaxLength(200);
        materialLot.Property(l => l.IqcInspectionNumber).HasMaxLength(50);
        materialLot.Property(l => l.Remark).HasMaxLength(500);
        materialLot.HasIndex(l => l.LotNumber).IsUnique();
        materialLot.HasIndex(l => new { l.MaterialCode, l.Status });
        materialLot.HasIndex(l => l.ReceivedAt);
        materialLot.HasQueryFilter(l => !l.IsDeleted);

        // ---------- SN 批次消耗（下游谱系） ----------
        var consumption = modelBuilder.Entity<SnMaterialConsumption>();
        consumption.ToTable("sn_material_consumptions");
        consumption.HasKey(c => c.Id);
        consumption.Property(c => c.Sn).HasMaxLength(100).IsRequired();
        consumption.Property(c => c.MaterialCode).HasMaxLength(100).IsRequired();
        consumption.Property(c => c.LotNumber).HasMaxLength(100).IsRequired();
        consumption.Property(c => c.Quantity).HasPrecision(18, 4);
        consumption.Property(c => c.OperationName).HasMaxLength(100);
        consumption.Property(c => c.EquipmentCode).HasMaxLength(50);
        consumption.Property(c => c.Remark).HasMaxLength(500);
        // 同一 SN + 批次 + 物料 只允许绑定一次（幂等）
        consumption.HasIndex(c => new { c.Sn, c.LotNumber, c.MaterialCode }).IsUnique();
        consumption.HasIndex(c => c.LotNumber);
        consumption.HasIndex(c => c.BoundAt);

        base.OnModelCreating(modelBuilder);
    }
}
