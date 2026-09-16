using Microsoft.EntityFrameworkCore;
using QiaoMES.Assistant.Domain;

namespace QiaoMES.Assistant.Infrastructure.Persistence;

/// <summary>
/// 智能问数的持久化上下文。<para>
/// 只存一张「运行时配置」单行表。<b>注意：问数本身（语义层 / 只读查询）走自己的只读连接，
/// 不经这里</b> —— 这个 DbContext 只负责配置读写，因此它注册在共享连接上、参与业务事务是安全的。
/// </para>
/// </summary>
public class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    public DbSet<AssistantSetting> Settings => Set<AssistantSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 每个模块一个 schema，与其它模块保持一致
        modelBuilder.HasDefaultSchema("assistant");

        var setting = modelBuilder.Entity<AssistantSetting>();
        setting.ToTable("settings");
        setting.HasKey(s => s.Id);
        setting.Property(s => s.LlmBaseUrl).HasMaxLength(300);
        setting.Property(s => s.LlmModel).HasMaxLength(100);
        setting.Property(s => s.LlmApiKeyProtected).HasMaxLength(1000);
        setting.Property(s => s.UpdatedBy).HasMaxLength(100);
        setting.Property(s => s.UpdatedAt).IsRequired();
        setting.HasIndex(s => s.UpdatedAt);

        base.OnModelCreating(modelBuilder);
    }
}
