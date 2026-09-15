using QiaoMES.MasterData.Domain;

namespace QiaoMES.MasterData.Application;

/// <summary>
/// 主数据只读查询实现。所有返回都是快照 DTO，调用方无法拿到可写的领域实体。
/// </summary>
public class MasterDataQueryService(
    ICatalogRepository catalogRepository,
    IRoutingRepository routingRepository,
    IBomRepository bomRepository) : IMasterDataQueryService
{
    public async Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await catalogRepository.GetByIdAsync<Product>(productId, cancellationToken);
        return product is null
            ? null
            : new ProductSnapshot(product.Id, product.Code, product.Name, product.IsActive);
    }

    public async Task<RoutingSnapshot?> GetActiveRoutingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var routing = await routingRepository.GetActiveByProductAsync(productId, cancellationToken);
        if (routing is null)
        {
            return null;
        }

        var steps = routing.OrderedSteps;
        var operationIds = steps.Select(s => s.OperationId).Distinct().ToList();
        var operations = operationIds.Count > 0
            ? (await catalogRepository.GetByIdsAsync<Operation>(operationIds, cancellationToken)).ToDictionary(o => o.Id)
            : [];

        var stepSnapshots = steps
            .Select(step =>
            {
                operations.TryGetValue(step.OperationId, out var operation);
                return new RoutingStepSnapshot(
                    step.Sequence,
                    step.OperationId,
                    operation?.Code ?? string.Empty,
                    operation?.Name ?? string.Empty,
                    step.WorkCenterId ?? operation?.DefaultWorkCenterId,
                    step.StandardSeconds > 0 ? step.StandardSeconds : operation?.StandardSeconds ?? 0,
                    step.IsQualityGate);
            })
            .ToList();

        return new RoutingSnapshot(routing.Id, routing.Version, stepSnapshots);
    }

    public async Task<BomSnapshot?> GetActiveBomAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var bom = await bomRepository.GetActiveByProductAsync(productId, cancellationToken);
        return bom is null ? null : new BomSnapshot(bom.Id, bom.Version, bom.Items.Count);
    }
}
