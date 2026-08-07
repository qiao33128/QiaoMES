using Microsoft.EntityFrameworkCore;
using QiaoMES.Identity.Domain;

namespace QiaoMES.Identity.Infrastructure.Persistence;

public class UserRepository(IdentityDbContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await db.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await db.Users
            .Include(u => u.Roles)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsUsernameTakenAsync(string username, CancellationToken cancellationToken = default)
    {
        return await db.Users.AnyAsync(u => u.Username == username, cancellationToken);
    }

    public void Add(User user) => db.Users.Add(user);

    public void Update(User user) => db.Users.Update(user);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
