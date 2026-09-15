using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Infrastructure.Authorization;
using QiaoMES.Infrastructure.Http;
using QiaoMES.MasterData.Application;
using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.MasterData.Api.Controllers;

/// <summary>
/// 主数据 CSV 导入导出。
/// <para>路由形如 <c>/api/master-data/products/export</c>，resource 取值：products / materials / operations / work-centers。</para>
/// </summary>
[ApiController]
[Route("api/master-data")]
[Authorize]
public class CatalogCsvController(ICatalogCsvService service) : ControllerBase
{
    [HttpGet("{resource}/export")]
    [HasPermission(Permissions.MasterData.Read)]
    public async Task<IActionResult> Export(string resource, CancellationToken cancellationToken)
    {
        var result = await service.ExportAsync(resource, cancellationToken);
        if (result.IsFailure)
        {
            return ApiResults.Problem(result.Error);
        }

        // 带 UTF-8 BOM，Excel 直接双击打开不乱码
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(result.Value))
            .ToArray();

        return File(bytes, "text/csv", $"{resource}-{DateTime.Now:yyyyMMddHHmmss}.csv");
    }

    [HttpPost("{resource}/import")]
    [HasPermission(Permissions.MasterData.Manage)]
    public async Task<IActionResult> Import(
        string resource,
        [FromBody] ImportCatalogCsvRequest request,
        CancellationToken cancellationToken)
        => ApiResults.FromResult(await service.ImportAsync(resource, request.Content, cancellationToken));
}
