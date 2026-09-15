using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Domain;

namespace QiaoMES.Identity.Infrastructure.Persistence;

/// <summary>
/// 身份模块数据库上下文。
/// </summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identity");

        var user = modelBuilder.Entity<User>();
        user.ToTable("users");
        user.HasKey(u => u.Id);
        user.Property(u => u.Username).HasMaxLength(50).IsRequired();
        user.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();
        user.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        user.Property(u => u.Email).HasMaxLength(200);
        user.HasIndex(u => u.Username).IsUnique();
        user.HasQueryFilter(u => !u.IsDeleted);

        var role = modelBuilder.Entity<Role>();
        role.ToTable("roles");
        role.HasKey(r => r.Id);
        role.Property(r => r.Name).HasMaxLength(50).IsRequired();
        role.Property(r => r.Description).HasMaxLength(255);
        role.HasIndex(r => r.Name).IsUnique();
        role.HasQueryFilter(r => !r.IsDeleted);

        var userRole = modelBuilder.Entity<UserRole>();
        userRole.ToTable("user_roles");
        userRole.HasKey(ur => ur.Id);
        userRole.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
        userRole.HasQueryFilter(ur => !ur.IsDeleted);

        var rolePermission = modelBuilder.Entity<RolePermission>();
        rolePermission.ToTable("role_permissions");
        rolePermission.HasKey(rp => rp.Id);
        rolePermission.Property(rp => rp.Permission).HasMaxLength(100).IsRequired();
        rolePermission.HasIndex(rp => new { rp.RoleId, rp.Permission }).IsUnique();
        rolePermission.HasQueryFilter(rp => !rp.IsDeleted);

        user.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(ur => ur.UserId);
        role.HasMany(r => r.Users)
            .WithOne()
            .HasForeignKey(ur => ur.RoleId);
        role.HasMany(r => r.Permissions)
            .WithOne()
            .HasForeignKey(rp => rp.RoleId);

        base.OnModelCreating(modelBuilder);
    }
}
