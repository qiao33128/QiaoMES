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
