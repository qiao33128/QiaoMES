using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Domain;

namespace QiaoMES.Identity.Infrastructure.Persistence;

public class RoleRepository(IdentityDbContext db) : IRoleRepository
{
    public async Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await db.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await db.Roles.AsNoTracking().ToListAsync(cancellationToken);
    }

    public void Add(Role role) => db.Roles.Add(role);
}
