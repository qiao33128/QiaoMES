using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Domain;

namespace QiaoMES.Identity.Infrastructure.Persistence;

public class RoleRepository(IdentityDbContext db) : IRoleRepository
{
    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await db.Roles
            .Include(r => r.Permissions)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsNameTakenAsync(string name, Guid? excludeRoleId = null, CancellationToken cancellationToken = default)
    {
        return await db.Roles.AnyAsync(
            r => r.Name == name && (excludeRoleId == null || r.Id != excludeRoleId),
            cancellationToken);
    }

    public void Add(Role role) => db.Roles.Add(role);

    public void Update(Role role) => db.Roles.Update(role);

    public void AddPermission(RolePermission permission) => db.RolePermissions.Add(permission);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
