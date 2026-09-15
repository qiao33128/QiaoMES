using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.Identity.Application;

/// <summary>
/// 角色管理服务。
/// </summary>
public interface IRoleService
{
    Task<Result<IReadOnlyList<RoleDto>>> GetListAsync(CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>整体替换角色权限，权限变更后立即对在线用户生效。</summary>
    Task<Result<RoleDto>> SetPermissionsAsync(Guid id, UpdateRolePermissionsRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>权限目录（按模块分组，供界面勾选）。</summary>
    Result<IReadOnlyList<PermissionGroupDto>> GetPermissionCatalog();
}
