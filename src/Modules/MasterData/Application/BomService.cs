using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

public class BomService(IBomRepository bomRepository, ICatalogRepository catalogRepository) : IBomService
{
    public async Task<Result<PagedResult<BomDto>>> GetListAsync(
        BomQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new BomQuery
        {
            ProductId = request.ProductId,
            IsActive = request.IsActive,
            Keyword = request.Keyword,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await bomRepository.QueryAsync(query, cancellationToken);
        var products = await LoadProductsAsync(items.Select(b => b.ProductId), cancellationToken);

        return Result.Success(new PagedResult<BomDto>
        {
            // 列表不返回明细内容，只带明细数
            Items = items.Select(b => ToDto(b, products, new Dictionary<Guid, Material>(), includeItems: false)).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<BomDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bom = await bomRepository.GetByIdAsync(id, cancellationToken);
        return bom is null
            ? Result.Failure<BomDto>(Error.NotFound("Bom.NotFound", "BOM 不存在"))
            : Result.Success(await BuildDtoAsync(bom, true, cancellationToken));
    }

    public async Task<Result<BomDto>> CreateAsync(CreateBomRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return Result.Failure<BomDto>(Error.Validation("Bom.InvalidVersion", "版本号不能为空"));
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Failure<BomDto>(Error.Validation("Bom.Empty", "BOM 至少需要一条明细"));
        }

        var productError = await ValidateProductAsync(request.ProductId, cancellationToken);
        if (productError is not null)
        {
            return Result.Failure<BomDto>(productError);
        }

        var version = request.Version.Trim();
        if (await bomRepository.IsVersionTakenAsync(request.ProductId, version, null, cancellationToken))
        {
            return Result.Failure<BomDto>(Error.Conflict("Bom.VersionTaken", $"版本 {version} 已存在"));
        }

        var materialError = await ValidateMaterialsAsync(request.Items, cancellationToken);
        if (materialError is not null)
        {
            return Result.Failure<BomDto>(materialError);
        }

        var bom = new Bom(request.ProductId, version, request.Remark);
        bom.ReplaceItems(ToSpecs(request.Items));

        // 新聚合：Add 会级联把明细一并标记为新增
        bomRepository.Add(bom);
        await bomRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildDtoAsync(bom, true, cancellationToken));
    }

    public async Task<Result<BomDto>> UpdateAsync(Guid id, UpdateBomRequest request, CancellationToken cancellationToken = default)
    {
        var bom = await bomRepository.GetByIdAsync(id, cancellationToken);
        if (bom is null)
        {
            return Result.Failure<BomDto>(Error.NotFound("Bom.NotFound", "BOM 不存在"));
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Failure<BomDto>(Error.Validation("Bom.Empty", "BOM 至少需要一条明细"));
        }

        var materialError = await ValidateMaterialsAsync(request.Items, cancellationToken);
        if (materialError is not null)
        {
            return Result.Failure<BomDto>(materialError);
        }

        bom.UpdateRemark(request.Remark);
        var added = bom.ReplaceItems(ToSpecs(request.Items));
        foreach (var item in added)
        {
            // 聚合已被跟踪：新明细必须显式 Add，否则 EF 会按 UPDATE 处理
            bomRepository.AddItem(item);
        }

        await bomRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildDtoAsync(bom, true, cancellationToken));
    }

    public async Task<Result<BomDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bom = await bomRepository.GetByIdAsync(id, cancellationToken);
        if (bom is null)
        {
            return Result.Failure<BomDto>(Error.NotFound("Bom.NotFound", "BOM 不存在"));
        }

        var result = bom.Activate();
        if (result.IsFailure)
        {
            return Result.Failure<BomDto>(result.Error);
        }

        // 同一产品只允许一个生效版本
        await bomRepository.DeactivateOtherVersionsAsync(bom.ProductId, bom.Id, cancellationToken);
        await bomRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildDtoAsync(bom, true, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bom = await bomRepository.GetByIdAsync(id, cancellationToken);
        if (bom is null)
        {
            return Result.Failure(Error.NotFound("Bom.NotFound", "BOM 不存在"));
        }
        if (bom.IsActive)
        {
            return Result.Failure(Error.Conflict("Bom.ActiveCannotDelete", "生效版本不能删除，请先激活其它版本"));
        }

        bomRepository.Remove(bom);
        await bomRepository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await catalogRepository.GetByIdAsync<Product>(productId, cancellationToken);
        if (product is null)
        {
            return Error.Validation("Bom.ProductNotFound", "产品不存在");
        }

        return product.IsActive ? null : Error.Conflict("Bom.ProductInactive", $"产品 {product.Code} 已停用");
    }

    private async Task<Error?> ValidateMaterialsAsync(
        IReadOnlyList<BomItemRequest> items,
        CancellationToken cancellationToken)
    {
        if (items.Any(i => i.Quantity <= 0))
        {
            return Error.Validation("Bom.InvalidQuantity", "用量必须大于 0");
        }
        if (items.Any(i => i.LossRate < 0))
        {
            return Error.Validation("Bom.InvalidLossRate", "损耗率不能为负数");
        }

        var ids = items.Select(i => i.MaterialId).Distinct().ToList();
        var materials = await catalogRepository.GetByIdsAsync<Material>(ids, cancellationToken);

        if (materials.Count != ids.Count)
        {
            return Error.Validation("Bom.MaterialNotFound", "存在无效的物料");
        }

        var inactive = materials.FirstOrDefault(m => !m.IsActive);
        return inactive is null
            ? null
            : Error.Conflict("Bom.MaterialInactive", $"物料 {inactive.Code} 已停用");
    }

    private static IReadOnlyList<BomItemSpec> ToSpecs(IReadOnlyList<BomItemRequest> items)
        => items.Select(i => new BomItemSpec(i.MaterialId, i.Quantity, i.Unit, i.LossRate, i.Remark)).ToList();

    private async Task<BomDto> BuildDtoAsync(Bom bom, bool includeItems, CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync([bom.ProductId], cancellationToken);
        var materials = new Dictionary<Guid, Material>();

        if (includeItems)
        {
            var ids = bom.Items.Select(i => i.MaterialId).Distinct().ToList();
            if (ids.Count > 0)
            {
                var loaded = await catalogRepository.GetByIdsAsync<Material>(ids, cancellationToken);
                materials = loaded.ToDictionary(m => m.Id);
            }
        }

        return ToDto(bom, products, materials, includeItems);
    }

    private async Task<Dictionary<Guid, Product>> LoadProductsAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var products = await catalogRepository.GetByIdsAsync<Product>(ids, cancellationToken);
        return products.ToDictionary(p => p.Id);
    }

    private static BomDto ToDto(
        Bom bom,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Material> materials,
        bool includeItems)
    {
        products.TryGetValue(bom.ProductId, out var product);

        var items = includeItems
            ? bom.Items
                .Select(item =>
                {
                    materials.TryGetValue(item.MaterialId, out var material);
                    return new BomItemDto(
                        item.Id,
                        item.MaterialId,
                        material?.Code ?? string.Empty,
                        material?.Name ?? string.Empty,
                        item.Quantity,
                        item.Unit,
                        item.LossRate,
                        item.RequiredQuantity,
                        item.Remark);
                })
                .ToList()
            : [];

        return new BomDto(
            bom.Id,
            bom.ProductId,
            product?.Code ?? string.Empty,
            product?.Name ?? string.Empty,
            bom.Version,
            bom.IsActive,
            bom.Remark,
            bom.Items.Count,
            items,
            bom.CreatedAt,
            bom.UpdatedAt);
    }
}
