using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Application.Contracts;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Identity.Api.Controllers;

/// <summary>
/// 角色与权限管理。查看需要 <c>roles:read</c>，变更需要 <c>roles:manage</c>。
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(IRoleService roleService) : ControllerBase
{
    /// <summary>权限目录（按模块分组），供角色管理界面勾选。</summary>
    [HttpGet("permissions")]
    [HasPermission(Permissions.Roles.Read)]
    public IActionResult GetPermissionCatalog()
        => ApiResults.FromResult(roleService.GetPermissionCatalog());

    [HttpGet]
    [HasPermission(Permissions.Roles.Read)]
    public async Task<IActionResult> GetList(CancellationToken cancellationToken)
        => ApiResults.FromResult(await roleService.GetListAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Roles.Read)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await roleService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await roleService.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await roleService.UpdateAsync(id, request, cancellationToken));

    /// <summary>整体替换角色的权限集合（变更立即对在线用户生效）。</summary>
    [HttpPut("{id:guid}/permissions")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> SetPermissions(Guid id, [FromBody] UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await roleService.SetPermissionsAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        => ApiResults.FromResult(await roleService.DeleteAsync(id, cancellationToken));
}
