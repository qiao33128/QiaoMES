using Microsoft.EntityFrameworkCore;
using QiaoMES.Equipment.Domain;

// 设备实体名与模块根命名空间同名，用别名消除解析歧义
using EquipmentEntity = QiaoMES.Equipment.Domain.Equipment;

namespace QiaoMES.Equipment.Infrastructure.Persistence;

/// <summary>
/// 设备模块数据库上下文（schema：<c>equipment</c>），同时承载 Andon 呼叫。
/// </summary>
public class EquipmentDbContext(DbContextOptions<EquipmentDbContext> options) : DbContext(options)
{
    public DbSet<EquipmentEntity> Equipments => Set<EquipmentEntity>();
    public DbSet<EquipmentStatusLog> StatusLogs => Set<EquipmentStatusLog>();
    public DbSet<EquipmentMaintenanceRecord> MaintenanceRecords => Set<EquipmentMaintenanceRecord>();
    public DbSet<AndonCall> AndonCalls => Set<AndonCall>();
    public DbSet<EquipmentDailySequence> DailySequences => Set<EquipmentDailySequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("equipment");

        var equipment = modelBuilder.Entity<EquipmentEntity>();
        equipment.ToTable("equipments");
        equipment.HasKey(e => e.Id);
        equipment.Property(e => e.Code).HasMaxLength(50).IsRequired();
        equipment.Property(e => e.Name).HasMaxLength(200).IsRequired();
        equipment.Property(e => e.Model).HasMaxLength(100);
        equipment.Property(e => e.SerialNumber).HasMaxLength(100);
        equipment.Property(e => e.LineName).HasMaxLength(100);
        equipment.Property(e => e.StatusReason).HasMaxLength(200);
        equipment.Property(e => e.DownReasonCode).HasMaxLength(50);
        equipment.Property(e => e.Remark).HasMaxLength(500);
        equipment.Property(e => e.Status).HasConversion<int>();
        equipment.HasIndex(e => e.Code).IsUnique();
        equipment.HasIndex(e => new { e.Status, e.IsActive });
        equipment.HasIndex(e => e.WorkCenterId);
        equipment.HasQueryFilter(e => !e.IsDeleted);
        equipment.HasMany(e => e.StatusLogs)
            .WithOne()
            .HasForeignKey(l => l.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        equipment.HasMany(e => e.MaintenanceRecords)
            .WithOne()
            .HasForeignKey(r => r.EquipmentId)
            .OnDelete(DeleteBehavior.Cascade);

        var statusLog = modelBuilder.Entity<EquipmentStatusLog>();
        statusLog.ToTable("equipment_status_logs");
        statusLog.HasKey(l => l.Id);
        statusLog.Property(l => l.ReasonCode).HasMaxLength(50);
        statusLog.Property(l => l.Reason).HasMaxLength(200);
        statusLog.Property(l => l.FromStatus).HasConversion<int>();
        statusLog.Property(l => l.ToStatus).HasConversion<int>();
        statusLog.HasIndex(l => l.EquipmentId);
        statusLog.HasIndex(l => l.ChangedAt);

        var maintenance = modelBuilder.Entity<EquipmentMaintenanceRecord>();
        maintenance.ToTable("equipment_maintenance_records");
        maintenance.HasKey(r => r.Id);
        maintenance.Property(r => r.Content).HasMaxLength(500).IsRequired();
        maintenance.Property(r => r.AbnormalDescription).HasMaxLength(500);
        maintenance.Property(r => r.Type).HasConversion<int>();
        maintenance.Property(r => r.Result).HasConversion<int>();
        maintenance.HasIndex(r => r.EquipmentId);
        maintenance.HasIndex(r => r.ExecutedAt);

        var andon = modelBuilder.Entity<AndonCall>();
        andon.ToTable("andon_calls");
        andon.HasKey(a => a.Id);
        andon.Property(a => a.CallNumber).HasMaxLength(50).IsRequired();
        andon.Property(a => a.EquipmentCode).HasMaxLength(50);
        andon.Property(a => a.WorkCenterName).HasMaxLength(100);
        andon.Property(a => a.Sn).HasMaxLength(100);
        andon.Property(a => a.Description).HasMaxLength(500).IsRequired();
        andon.Property(a => a.Resolution).HasMaxLength(500);
        andon.Property(a => a.Type).HasConversion<int>();
        andon.Property(a => a.Level).HasConversion<int>();
        andon.Property(a => a.Status).HasConversion<int>();
        andon.HasIndex(a => a.CallNumber).IsUnique();
        andon.HasIndex(a => new { a.Status, a.Level });
        andon.HasIndex(a => a.CalledAt);
        andon.HasIndex(a => a.EquipmentId);

        var sequence = modelBuilder.Entity<EquipmentDailySequence>();
        sequence.ToTable("equipment_number_sequences");
        sequence.HasKey(s => s.SequenceKey);
        sequence.Property(s => s.SequenceKey).HasColumnName("sequence_key").HasMaxLength(50);
        sequence.Property(s => s.LastValue).HasColumnName("last_value").IsRequired();
        sequence.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>Andon 单号日序列（数据库原子分配）。</summary>
public class EquipmentDailySequence
{
    private EquipmentDailySequence() { }

    public string SequenceKey { get; private set; } = string.Empty;

    public int LastValue { get; private set; }

    public DateTime UpdatedAt { get; private set; }
}
