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

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> QueryAsync(
        UserQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<User> users = db.Users
            .AsNoTracking()
            .Include(u => u.Roles);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var pattern = $"%{EscapeLikePattern(query.Keyword.Trim())}%";
            users = users.Where(u =>
                EF.Functions.ILike(u.Username, pattern, "\\") ||
                EF.Functions.ILike(u.DisplayName, pattern, "\\"));
        }

        if (query.IsActive is not null)
        {
            users = users.Where(u => u.IsActive == query.IsActive);
        }

        var totalCount = await users.CountAsync(cancellationToken);

        var items = await users
            .OrderBy(u => u.Username)
            .Skip(query.Skip)
            .Take(query.NormalizedPageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<bool> IsUsernameTakenAsync(string username, CancellationToken cancellationToken = default)
    {
        return await db.Users.AnyAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetUserIdsByRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        return await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.RoleId == roleId && !ur.IsDeleted)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public void Add(User user) => db.Users.Add(user);

    public void Update(User user) => db.Users.Update(user);

    public void AddRoleLink(UserRole userRole) => db.UserRoles.Add(userRole);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    private static string EscapeLikePattern(string input)
        => input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
