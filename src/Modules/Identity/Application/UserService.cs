using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Identity.Domain;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Application;

public class UserService(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    IPasswordHasher passwordHasher,
    PermissionCacheEpoch epoch) : IUserService
{
    public async Task<Result<PagedResult<AdminUserDto>>> GetListAsync(
        PaginationRequest pagination,
        string? keyword = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = new UserQuery
        {
            Keyword = keyword,
            IsActive = isActive,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
        };

        var (items, totalCount) = await userRepository.QueryAsync(query, cancellationToken);
        var roleNames = await ResolveRoleNamesAsync(cancellationToken);

        return Result.Success(new PagedResult<AdminUserDto>
        {
            Items = items.Select(user => ToDto(user, roleNames)).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<AdminUserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<AdminUserDto>(Error.Validation("User.InvalidInput", "用户名和密码不能为空"));
        }
        if (request.Password.Length < 6)
        {
            return Result.Failure<AdminUserDto>(Error.Validation("User.WeakPassword", "密码长度至少 6 位"));
        }

        var username = request.Username.Trim();
        if (await userRepository.IsUsernameTakenAsync(username, cancellationToken))
        {
            return Result.Failure<AdminUserDto>(Error.Conflict("User.UsernameTaken", "用户名已被占用"));
        }

        var allRoles = await roleRepository.GetAllAsync(cancellationToken);
        var roleIds = request.RoleIds ?? [];
        var invalid = roleIds.Where(id => allRoles.All(r => r.Id != id)).ToList();
        if (invalid.Count > 0)
        {
            return Result.Failure<AdminUserDto>(Error.Validation("User.UnknownRole", "存在无效的角色"));
        }

        var user = new User(username, passwordHasher.Hash(request.Password), request.DisplayName.Trim(), request.Email);
        user.ReplaceRoles(roleIds);

        userRepository.Add(user);
        await userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(user, allRoles.ToDictionary(r => r.Id, r => r.Name)));
    }

    public async Task<Result<AdminUserDto>> SetRolesAsync(Guid id, UpdateUserRolesRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminUserDto>(Error.NotFound("User.NotFound", "用户不存在"));
        }

        var allRoles = await roleRepository.GetAllAsync(cancellationToken);
        var roleIds = request.RoleIds ?? [];
        var invalid = roleIds.Where(roleId => allRoles.All(r => r.Id != roleId)).ToList();
        if (invalid.Count > 0)
        {
            return Result.Failure<AdminUserDto>(Error.Validation("User.UnknownRole", "存在无效的角色"));
        }

        // 实体由仓储加载并处于变更跟踪中；新建的关联必须显式 Add
        var added = user.ReplaceRoles(roleIds);
        foreach (var link in added)
        {
            userRepository.AddRoleLink(link);
        }

        await userRepository.SaveChangesAsync(cancellationToken);

        // 角色变化：立即刷新权限缓存，在线用户无需重新登录即可获得新权限
        epoch.Bump();
        return Result.Success(ToDto(user, allRoles.ToDictionary(r => r.Id, r => r.Name)));
    }

    public async Task<Result<AdminUserDto>> SetActiveAsync(Guid id, SetUserActiveRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AdminUserDto>(Error.NotFound("User.NotFound", "用户不存在"));
        }

        user.SetActive(request.IsActive);
        await userRepository.SaveChangesAsync(cancellationToken);

        // 停用后权限解析返回空集合，因此现有令牌会立即失去全部业务权限
        epoch.Bump();

        var roleNames = await ResolveRoleNamesAsync(cancellationToken);
        return Result.Success(ToDto(user, roleNames));
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ResolveRoleNamesAsync(CancellationToken cancellationToken)
    {
        var roles = await roleRepository.GetAllAsync(cancellationToken);
        return roles.ToDictionary(r => r.Id, r => r.Name);
    }

    private static AdminUserDto ToDto(User user, IReadOnlyDictionary<Guid, string> roleNames)
        => new(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Email,
            user.IsActive,
            user.CreatedAt,
            user.LastLoginAt,
            user.RoleIds,
            user.RoleIds
                .Where(roleNames.ContainsKey)
                .Select(roleId => roleNames[roleId])
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList());
}
