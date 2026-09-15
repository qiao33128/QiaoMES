using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Identity.Domain;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Application;

public class RoleService(
    IRoleRepository roleRepository,
    IUserRepository userRepository,
    PermissionCacheEpoch epoch) : IRoleService
{
    public async Task<Result<IReadOnlyList<RoleDto>>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var roles = await roleRepository.GetAllAsync(cancellationToken);
        return Result.Success<IReadOnlyList<RoleDto>>(roles.Select(ToDto).ToList());
    }

    public async Task<Result<RoleDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        return role is null
            ? Result.Failure<RoleDto>(Error.NotFound("Role.NotFound", "角色不存在"))
            : Result.Success(ToDto(role));
    }

    public async Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<RoleDto>(Error.Validation("Role.InvalidName", "角色名不能为空"));
        }

        var name = request.Name.Trim();
        if (await roleRepository.IsNameTakenAsync(name, null, cancellationToken))
        {
            return Result.Failure<RoleDto>(Error.Conflict("Role.NameTaken", "角色名已存在"));
        }

        var unknownPermission = ValidatePermissions(request.Permissions);
        if (unknownPermission is not null)
        {
            return Result.Failure<RoleDto>(unknownPermission);
        }

        var role = new Role(name, request.Description ?? string.Empty);
        foreach (var permission in request.Permissions ?? [])
        {
            role.GrantPermission(permission);
        }

        roleRepository.Add(role);
        await roleRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(role));
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(Error.NotFound("Role.NotFound", "角色不存在"));
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<RoleDto>(Error.Validation("Role.InvalidName", "角色名不能为空"));
        }

        var name = request.Name.Trim();
        if (BuiltInRoles.IsBuiltIn(role.Name) && !string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<RoleDto>(Error.Conflict("Role.BuiltInRenameForbidden", "内置角色不允许改名"));
        }
        if (await roleRepository.IsNameTakenAsync(name, id, cancellationToken))
        {
            return Result.Failure<RoleDto>(Error.Conflict("Role.NameTaken", "角色名已存在"));
        }

        // 实体由仓储加载，处于变更跟踪中，直接修改属性即可，无需再 Update
        role.Rename(name, request.Description ?? string.Empty);
        await roleRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(role));
    }

    public async Task<Result<RoleDto>> SetPermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role is null)
        {
            return Result.Failure<RoleDto>(Error.NotFound("Role.NotFound", "角色不存在"));
        }

        var permissions = request.Permissions ?? [];
        var unknownPermission = ValidatePermissions(permissions);
        if (unknownPermission is not null)
        {
            return Result.Failure<RoleDto>(unknownPermission);
        }

        var added = role.ReplacePermissions(permissions);
        foreach (var permission in added)
        {
            roleRepository.AddPermission(permission);
        }

        await roleRepository.SaveChangesAsync(cancellationToken);

        // 角色权限已变化：递增代次让所有在线用户的权限缓存立即失效
        epoch.Bump();
        return Result.Success(ToDto(role));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await roleRepository.GetByIdAsync(id, cancellationToken);
        if (role is null)
        {
            return Result.Failure(Error.NotFound("Role.NotFound", "角色不存在"));
        }
        if (BuiltInRoles.IsBuiltIn(role.Name))
        {
            return Result.Failure(Error.Conflict("Role.BuiltInDeleteForbidden", "内置角色不允许删除"));
        }

        var users = await userRepository.GetUserIdsByRoleAsync(id, cancellationToken);
        if (users.Count > 0)
        {
            return Result.Failure(Error.Conflict("Role.InUse", $"该角色仍被 {users.Count} 个用户使用，请先解除关联"));
        }

        // 软删除：实体已被跟踪，SaveChanges 会生成 UPDATE
        role.Delete();
        await roleRepository.SaveChangesAsync(cancellationToken);
        epoch.Bump();
        return Result.Success();
    }

    public Result<IReadOnlyList<PermissionGroupDto>> GetPermissionCatalog()
        => Result.Success<IReadOnlyList<PermissionGroupDto>>(
            PermissionCatalog.Groups
                .Select(group => new PermissionGroupDto(
                    group.Group,
                    group.Items.Select(item => new PermissionItemDto(item.Code, item.Name)).ToList()))
                .ToList());

    private static Error? ValidatePermissions(IEnumerable<string>? permissions)
    {
        if (permissions is null)
        {
            return null;
        }

        var unknown = permissions
            .Where(p => !string.IsNullOrWhiteSpace(p) && !Permissions.IsDefined(p.Trim()))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return unknown.Count == 0
            ? null
            : Error.Validation("Role.UnknownPermission", $"存在未登记的权限：{string.Join(", ", unknown)}");
    }

    private static RoleDto ToDto(Role role)
        => new(role.Id, role.Name, role.Description, BuiltInRoles.IsBuiltIn(role.Name), role.PermissionNames);
}
