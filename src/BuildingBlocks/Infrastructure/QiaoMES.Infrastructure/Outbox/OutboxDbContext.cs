using Microsoft.EntityFrameworkCore;

namespace QiaoMES.Infrastructure.Outbox;

/// <summary>
/// Outbox 存储（schema：<c>infrastructure</c>）。<para>
/// 与业务 DbContext 共享同一连接，因此 <c>UnitOfWorkFilter</c> 会把 Outbox 写入自动纳入同一事务。
/// </para>
/// </summary>
public class OutboxDbContext(DbContextOptions<OutboxDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("infrastructure");

        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        outbox.HasKey(m => m.Id);
        outbox.Property(m => m.EventType).HasMaxLength(300).IsRequired();
        outbox.Property(m => m.Payload).IsRequired();
        outbox.Property(m => m.Status).HasConversion<int>();
        outbox.Property(m => m.LastError).HasMaxLength(1000);
        // 分发器按「状态 + 下次重试时间」轮询，索引直接服务该查询
        outbox.HasIndex(m => new { m.Status, m.NextRetryAt });
        outbox.HasIndex(m => m.OccurredAt);

        base.OnModelCreating(modelBuilder);
    }
}
