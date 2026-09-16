using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.Infrastructure.Integrations;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Api.Controllers;

/// <summary>
/// 开放 API 客户端管理（内部管理员使用 JWT 调用）。<para>
/// 明文密钥只在创建时返回一次，之后仅保留 SHA-256 摘要与可识别前缀。
/// </para>
/// </summary>
[ApiController]
[Route("api/integration/api-clients")]
[Authorize]
public class ApiClientsController(IApiClientService service) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Integration.Read)]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => ApiResults.FromResult(await service.GetListAsync(page, pageSize, cancellationToken));

    /// <summary>发放新的 API 密钥（明文仅在本次响应中返回）。</summary>
    [HttpPost]
    [HasPermission(Permissions.Integration.Manage)]
    public async Task<IActionResult> Create([FromBody] CreateApiClientRequest request, CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.CreateAsync(request, cancellationToken));

    /// <summary>停用 / 启用客户端（停用后该密钥立即失效）。</summary>
    [HttpPut("{id:guid}/active")]
    [HasPermission(Permissions.Integration.Manage)]
    public async Task<IActionResult> SetActive(
        Guid id,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.SetActiveAsync(id, isActive, cancellationToken));
}
