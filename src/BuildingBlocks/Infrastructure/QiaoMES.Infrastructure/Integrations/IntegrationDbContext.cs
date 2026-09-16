using Microsoft.EntityFrameworkCore;

namespace QiaoMES.Infrastructure.Integrations;

/// <summary>对外集成存储（schema：<c>integration</c>）。</summary>
public class IntegrationDbContext(DbContextOptions<IntegrationDbContext> options) : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("integration");

        var client = modelBuilder.Entity<ApiClient>();
        client.ToTable("api_clients");
        client.HasKey(c => c.Id);
        client.Property(c => c.Name).HasMaxLength(100).IsRequired();
        client.Property(c => c.ApiKeyHash).HasMaxLength(64).IsRequired();
        client.Property(c => c.KeyPreview).HasMaxLength(32).IsRequired();
        client.Property(c => c.Scopes).HasMaxLength(300).IsRequired();
        client.Property(c => c.Remark).HasMaxLength(500);
        client.HasIndex(c => c.ApiKeyHash).IsUnique();
        client.HasIndex(c => c.Name);
        client.HasQueryFilter(c => !c.IsDeleted);

        base.OnModelCreating(modelBuilder);
    }
}
