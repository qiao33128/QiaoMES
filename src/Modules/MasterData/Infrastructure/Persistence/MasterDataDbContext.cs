using Microsoft.EntityFrameworkCore;
using QiaoMES.MasterData.Domain;

namespace QiaoMES.MasterData.Infrastructure.Persistence;

/// <summary>
/// 主数据模块数据库上下文。
/// </summary>
public class MasterDataDbContext(DbContextOptions<MasterDataDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<WorkCenter> WorkCenters => Set<WorkCenter>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<BomItem> BomItems => Set<BomItem>();
    public DbSet<Routing> Routings => Set<Routing>();
    public DbSet<RoutingStep> RoutingSteps => Set<RoutingStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("masterdata");

        // 四种主数据共享 CatalogEntity 的字段与索引约定
        ConfigureCatalog<Product>(modelBuilder, "products");
        ConfigureCatalog<Material>(modelBuilder, "materials");
        ConfigureCatalog<WorkCenter>(modelBuilder, "work_centers");
        ConfigureCatalog<Operation>(modelBuilder, "operations");

        var material = modelBuilder.Entity<Material>();
        material.Property(m => m.MaterialType).HasConversion<int>();
        material.Property(m => m.SupplierPartNumber).HasMaxLength(100);

        var workCenter = modelBuilder.Entity<WorkCenter>();
        workCenter.Property(w => w.Type).HasConversion<int>();
        workCenter.Property(w => w.Workshop).HasMaxLength(100);

        var operation = modelBuilder.Entity<Operation>();
        operation.Property(o => o.StandardSeconds).IsRequired();
        operation.Property(o => o.IsKeyOperation).IsRequired();

        // ---------- BOM ----------
        var bom = modelBuilder.Entity<Bom>();
        bom.ToTable("boms");
        bom.HasKey(b => b.Id);
        bom.Property(b => b.Version).HasMaxLength(50).IsRequired();
        bom.Property(b => b.Remark).HasMaxLength(500);
        // 同一产品下版本唯一
        bom.HasIndex(b => new { b.ProductId, b.Version }).IsUnique();
        bom.HasQueryFilter(b => !b.IsDeleted);
        bom.HasMany(b => b.Items)
            .WithOne()
            .HasForeignKey(i => i.BomId)
            .OnDelete(DeleteBehavior.Cascade);

        var bomItem = modelBuilder.Entity<BomItem>();
        bomItem.ToTable("bom_items");
        bomItem.HasKey(i => i.Id);
        bomItem.Property(i => i.Unit).HasMaxLength(20);
        bomItem.Property(i => i.Remark).HasMaxLength(500);
        bomItem.Property(i => i.Quantity).HasPrecision(18, 6);
        bomItem.Property(i => i.LossRate).HasPrecision(9, 6);
        bomItem.HasIndex(i => i.BomId);
        bomItem.HasIndex(i => i.MaterialId);

        // ---------- 工艺路线 ----------
        var routing = modelBuilder.Entity<Routing>();
        routing.ToTable("routings");
        routing.HasKey(r => r.Id);
        routing.Property(r => r.Version).HasMaxLength(50).IsRequired();
        routing.Property(r => r.Remark).HasMaxLength(500);
        routing.HasIndex(r => new { r.ProductId, r.Version }).IsUnique();
        routing.HasQueryFilter(r => !r.IsDeleted);
        routing.HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.RoutingId)
            .OnDelete(DeleteBehavior.Cascade);

        var routingStep = modelBuilder.Entity<RoutingStep>();
        routingStep.ToTable("routing_steps");
        routingStep.HasKey(s => s.Id);
        routingStep.HasIndex(s => s.RoutingId);
        routingStep.HasIndex(s => new { s.RoutingId, s.Sequence });

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureCatalog<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : CatalogEntity
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.ToTable(tableName);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Code).HasMaxLength(50).IsRequired();
        entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
        entity.Property(e => e.Spec).HasMaxLength(200);
        entity.Property(e => e.Unit).HasMaxLength(20);
        entity.Property(e => e.Remark).HasMaxLength(500);
        entity.HasIndex(e => e.Code).IsUnique();
        // 列表页默认「启用状态 + 编码排序」，建立组合索引避免全表排序
        entity.HasIndex(e => new { e.IsActive, e.Code });
        entity.HasQueryFilter(e => !e.IsDeleted);
    }
}
