using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.Identity.Application;

/// <summary>
/// 用户管理服务（管理员视角）。
/// </summary>
public interface IUserService
{
    Task<Result<PagedResult<AdminUserDto>>> GetListAsync(
        PaginationRequest pagination,
        string? keyword = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default);

    Task<Result<AdminUserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>整体替换用户角色，变更后立即生效（授权判定以数据库为准）。</summary>
    Task<Result<AdminUserDto>> SetRolesAsync(Guid id, UpdateUserRolesRequest request, CancellationToken cancellationToken = default);

    /// <summary>启用 / 停用用户。</summary>
    Task<Result<AdminUserDto>> SetActiveAsync(Guid id, SetUserActiveRequest request, CancellationToken cancellationToken = default);
}
