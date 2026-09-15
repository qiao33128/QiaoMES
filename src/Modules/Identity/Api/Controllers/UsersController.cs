using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Api.Controllers;

/// <summary>
/// 用户管理。查看需要 <c>users:read</c>，变更需要 <c>users:manage</c>。
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await userService.GetListAsync(
            new PaginationRequest(page, pageSize), keyword, isActive, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await userService.CreateAsync(request, cancellationToken));

    /// <summary>整体替换用户角色（变更立即生效）。</summary>
    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> SetRoles(Guid id, [FromBody] UpdateUserRolesRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await userService.SetRolesAsync(id, request, cancellationToken));

    /// <summary>启用 / 停用用户（停用后立即失去全部业务权限）。</summary>
    [HttpPut("{id:guid}/status")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> SetStatus(Guid id, [FromBody] SetUserActiveRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await userService.SetActiveAsync(id, request, cancellationToken));
}
