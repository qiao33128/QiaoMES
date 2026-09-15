using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

public class RoutingService(IRoutingRepository routingRepository, ICatalogRepository catalogRepository) : IRoutingService
{
    public async Task<Result<PagedResult<RoutingDto>>> GetListAsync(
        RoutingQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new RoutingQuery
        {
            ProductId = request.ProductId,
            IsActive = request.IsActive,
            Keyword = request.Keyword,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await routingRepository.QueryAsync(query, cancellationToken);
        var products = await LoadProductsAsync(items.Select(r => r.ProductId), cancellationToken);

        return Result.Success(new PagedResult<RoutingDto>
        {
            Items = items
                .Select(r => ToDto(r, products, new Dictionary<Guid, Operation>(),
                    new Dictionary<Guid, WorkCenter>(), includeSteps: false))
                .ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<RoutingDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var routing = await routingRepository.GetByIdAsync(id, cancellationToken);
        return routing is null
            ? Result.Failure<RoutingDto>(Error.NotFound("Routing.NotFound", "工艺路线不存在"))
            : Result.Success(await BuildDtoAsync(routing, true, cancellationToken));
    }

    public async Task<Result<RoutingDto>> CreateAsync(CreateRoutingRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return Result.Failure<RoutingDto>(Error.Validation("Routing.InvalidVersion", "版本号不能为空"));
        }
        if (request.Steps is null || request.Steps.Count == 0)
        {
            return Result.Failure<RoutingDto>(Error.Validation("Routing.Empty", "工艺路线至少需要一个工序"));
        }

        var productError = await ValidateProductAsync(request.ProductId, cancellationToken);
        if (productError is not null)
        {
            return Result.Failure<RoutingDto>(productError);
        }

        var version = request.Version.Trim();
        if (await routingRepository.IsVersionTakenAsync(request.ProductId, version, null, cancellationToken))
        {
            return Result.Failure<RoutingDto>(Error.Conflict("Routing.VersionTaken", $"版本 {version} 已存在"));
        }

        var stepError = await ValidateStepsAsync(request.Steps, cancellationToken);
        if (stepError is not null)
        {
            return Result.Failure<RoutingDto>(stepError);
        }

        var routing = new Routing(request.ProductId, version, request.Remark);
        routing.ReplaceSteps(ToSpecs(request.Steps));

        routingRepository.Add(routing);
        await routingRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildDtoAsync(routing, true, cancellationToken));
    }

    public async Task<Result<RoutingDto>> UpdateAsync(Guid id, UpdateRoutingRequest request, CancellationToken cancellationToken = default)
    {
        var routing = await routingRepository.GetByIdAsync(id, cancellationToken);
        if (routing is null)
        {
            return Result.Failure<RoutingDto>(Error.NotFound("Routing.NotFound", "工艺路线不存在"));
        }
        if (request.Steps is null || request.Steps.Count == 0)
        {
            return Result.Failure<RoutingDto>(Error.Validation("Routing.Empty", "工艺路线至少需要一个工序"));
        }

        var stepError = await ValidateStepsAsync(request.Steps, cancellationToken);
        if (stepError is not null)
        {
            return Result.Failure<RoutingDto>(stepError);
        }

        routing.UpdateRemark(request.Remark);
        var added = routing.ReplaceSteps(ToSpecs(request.Steps));
        foreach (var step in added)
        {
            routingRepository.AddStep(step);
        }

        await routingRepository.SaveChangesAsync(cancellationToken);
        return Result.Success(await BuildDtoAsync(routing, true, cancellationToken));
    }

    public async Task<Result<RoutingDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var routing = await routingRepository.GetByIdAsync(id, cancellationToken);
        if (routing is null)
        {
            return Result.Failure<RoutingDto>(Error.NotFound("Routing.NotFound", "工艺路线不存在"));
        }

        var result = routing.Activate();
        if (result.IsFailure)
        {
            return Result.Failure<RoutingDto>(result.Error);
        }

        await routingRepository.DeactivateOtherVersionsAsync(routing.ProductId, routing.Id, cancellationToken);
        await routingRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(await BuildDtoAsync(routing, true, cancellationToken));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var routing = await routingRepository.GetByIdAsync(id, cancellationToken);
        if (routing is null)
        {
            return Result.Failure(Error.NotFound("Routing.NotFound", "工艺路线不存在"));
        }
        if (routing.IsActive)
        {
            return Result.Failure(Error.Conflict("Routing.ActiveCannotDelete", "生效版本不能删除，请先激活其它版本"));
        }

        routingRepository.Remove(routing);
        await routingRepository.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await catalogRepository.GetByIdAsync<Product>(productId, cancellationToken);
        if (product is null)
        {
            return Error.Validation("Routing.ProductNotFound", "产品不存在");
        }

        return product.IsActive ? null : Error.Conflict("Routing.ProductInactive", $"产品 {product.Code} 已停用");
    }

    private async Task<Error?> ValidateStepsAsync(
        IReadOnlyList<RoutingStepRequest> steps,
        CancellationToken cancellationToken)
    {
        if (steps.Any(s => s.StandardSeconds < 0))
        {
            return Error.Validation("Routing.InvalidStandardTime", "标准工时不能为负数");
        }
        if (steps.GroupBy(s => s.OperationId).Any(g => g.Count() > 1))
        {
            return Error.Validation("Routing.DuplicateOperation", "同一工序在路线中重复出现");
        }

        var operationIds = steps.Select(s => s.OperationId).Distinct().ToList();
        var operations = await catalogRepository.GetByIdsAsync<Operation>(operationIds, cancellationToken);
        if (operations.Count != operationIds.Count)
        {
            return Error.Validation("Routing.OperationNotFound", "存在无效的工序");
        }

        var inactiveOperation = operations.FirstOrDefault(o => !o.IsActive);
        if (inactiveOperation is not null)
        {
            return Error.Conflict("Routing.OperationInactive", $"工序 {inactiveOperation.Code} 已停用");
        }

        var workCenterIds = steps
            .Where(s => s.WorkCenterId is not null)
            .Select(s => s.WorkCenterId!.Value)
            .Distinct()
            .ToList();

        if (workCenterIds.Count > 0)
        {
            var workCenters = await catalogRepository.GetByIdsAsync<WorkCenter>(workCenterIds, cancellationToken);
            if (workCenters.Count != workCenterIds.Count)
            {
                return Error.Validation("Routing.WorkCenterNotFound", "存在无效的工作中心");
            }
        }

        return null;
    }

    private static IReadOnlyList<RoutingStepSpec> ToSpecs(IReadOnlyList<RoutingStepRequest> steps)
        => steps
            .Select(s => new RoutingStepSpec(s.Sequence, s.OperationId, s.WorkCenterId, s.StandardSeconds, s.IsQualityGate))
            .ToList();

    private async Task<RoutingDto> BuildDtoAsync(Routing routing, bool includeSteps, CancellationToken cancellationToken)
    {
        var products = await LoadProductsAsync([routing.ProductId], cancellationToken);
        var operations = new Dictionary<Guid, Operation>();
        var workCenters = new Dictionary<Guid, WorkCenter>();

        if (includeSteps)
        {
            var steps = routing.OrderedSteps;

            var operationIds = steps.Select(s => s.OperationId).Distinct().ToList();
            if (operationIds.Count > 0)
            {
                var loaded = await catalogRepository.GetByIdsAsync<Operation>(operationIds, cancellationToken);
                operations = loaded.ToDictionary(o => o.Id);
            }

            var workCenterIds = steps
                .Where(s => s.WorkCenterId is not null)
                .Select(s => s.WorkCenterId!.Value)
                .Distinct()
                .ToList();

            if (workCenterIds.Count > 0)
            {
                var loaded = await catalogRepository.GetByIdsAsync<WorkCenter>(workCenterIds, cancellationToken);
                workCenters = loaded.ToDictionary(w => w.Id);
            }
        }

        return ToDto(routing, products, operations, workCenters, includeSteps);
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

    private static RoutingDto ToDto(
        Routing routing,
        IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyDictionary<Guid, Operation> operations,
        IReadOnlyDictionary<Guid, WorkCenter> workCenters,
        bool includeSteps)
    {
        products.TryGetValue(routing.ProductId, out var product);

        var steps = includeSteps
            ? routing.OrderedSteps
                .Select(step =>
                {
                    operations.TryGetValue(step.OperationId, out var operation);
                    WorkCenter? workCenter = null;
                    if (step.WorkCenterId is not null)
                    {
                        workCenters.TryGetValue(step.WorkCenterId.Value, out workCenter);
                    }

                    return new RoutingStepDto(
                        step.Id,
                        step.Sequence,
                        step.OperationId,
                        operation?.Code ?? string.Empty,
                        operation?.Name ?? string.Empty,
                        step.WorkCenterId,
                        workCenter?.Code,
                        workCenter?.Name,
                        step.StandardSeconds,
                        step.IsQualityGate);
                })
                .ToList()
            : [];

        return new RoutingDto(
            routing.Id,
            routing.ProductId,
            product?.Code ?? string.Empty,
            product?.Name ?? string.Empty,
            routing.Version,
            routing.IsActive,
            routing.Remark,
            routing.OrderedSteps.Count,
            routing.TotalStandardSeconds,
            steps,
            routing.CreatedAt,
            routing.UpdatedAt);
    }
}
