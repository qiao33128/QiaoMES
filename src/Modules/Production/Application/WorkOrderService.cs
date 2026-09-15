using QiaoMES.MasterData.Application;
using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application;

public class WorkOrderService(
    IWorkOrderRepository repository,
    IWorkOrderNumberGenerator numberGenerator,
    IWorkOrderNotifier notifier,
    IPostCommitActions postCommit,
    IMasterDataQueryService masterData,
    ICurrentUser currentUser) : IWorkOrderService
{
    public async Task<Result<WorkOrderDto>> CreateAsync(
        CreateWorkOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PlannedQuantity <= 0)
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.InvalidQuantity", "计划数量必须大于 0"));
        }

        var product = await masterData.GetProductAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.ProductNotFound", "产品不存在"));
        }
        if (!product.IsActive)
        {
            return Result.Failure<WorkOrderDto>(Error.Conflict("WorkOrder.ProductInactive", $"产品 {product.Code} 已停用"));
        }

        // 单号由数据库原子生成，并发安全
        var orderNumber = await numberGenerator.NextAsync(DateTime.Now, cancellationToken);
        var workOrder = new WorkOrder(
            orderNumber,
            product.Id,
            product.Code,
            product.Name,
            request.PlannedQuantity,
            request.PlannedStart,
            request.PlannedEnd,
            request.WorkCenter,
            request.Remark);

        repository.Add(workOrder);
        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        EnqueueNotification(dto, "created");
        return Result.Success(dto);
    }

    public async Task<Result<WorkOrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        return workOrder is null ? NotFound() : Result.Success(ToDto(workOrder));
    }

    public async Task<Result<PagedResult<WorkOrderDto>>> GetListAsync(
        PaginationRequest pagination,
        WorkOrderStatus? status,
        string? keyword,
        CancellationToken cancellationToken = default)
    {
        var query = new WorkOrderQuery
        {
            Status = status,
            Keyword = keyword,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        return Result.Success(new PagedResult<WorkOrderDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<WorkOrderDto>> UpdateAsync(
        Guid id,
        UpdateWorkOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status != WorkOrderStatus.Draft)
        {
            return Result.Failure<WorkOrderDto>(Error.Conflict("WorkOrder.NotEditable", "只有草稿状态的工单才能编辑"));
        }
        if (request.PlannedQuantity <= 0)
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.InvalidQuantity", "计划数量必须大于 0"));
        }

        var product = await masterData.GetProductAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.ProductNotFound", "产品不存在"));
        }

        workOrder.UpdatePlan(product.Id, product.Code, product.Name, request.PlannedQuantity,
            request.PlannedStart, request.PlannedEnd, request.WorkCenter, request.Remark);
        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        EnqueueNotification(dto, "updated");
        return Result.Success(dto);
    }

    /// <summary>
    /// 下达：取产品当前生效的工艺路线展开工序任务，并快照 BOM / 工艺路线版本。
    /// </summary>
    public async Task<Result<WorkOrderDto>> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status != WorkOrderStatus.Draft)
        {
            return Result.Failure<WorkOrderDto>(Error.Conflict("WorkOrder.InvalidTransition", "只有草稿状态的工单才能下达"));
        }

        var routing = await masterData.GetActiveRoutingAsync(workOrder.ProductId, cancellationToken);
        if (routing is null || routing.Steps.Count == 0)
        {
            return Result.Failure<WorkOrderDto>(Error.Conflict(
                "WorkOrder.NoRouting",
                $"产品 {workOrder.ProductCode} 没有生效的工艺路线，无法下达"));
        }

        var bom = await masterData.GetActiveBomAsync(workOrder.ProductId, cancellationToken);
        var snapshot = ToReleaseSnapshot(routing, bom);

        var result = workOrder.Release(snapshot);
        if (result.IsFailure)
        {
            return Result.Failure<WorkOrderDto>(result.Error);
        }

        // 工序任务是聚合内的新实体，必须显式持久化
        foreach (var operation in workOrder.Operations)
        {
            repository.AddOperation(operation);
        }

        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        EnqueueNotification(dto, "released");
        return Result.Success(dto);
    }

    public async Task<Result<WorkOrderDto>> StartProductionAsync(Guid id, CancellationToken cancellationToken = default)
        => await TransitionAsync(id, "started", w => w.StartProduction(), cancellationToken);

    public async Task<Result<WorkOrderDto>> ReportOperationAsync(
        Guid id,
        Guid operationTaskId,
        ReportOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var result = workOrder.ReportOperation(
            operationTaskId, request.GoodQuantity, request.DefectQuantity, request.ScrapQuantity);
        if (result.IsFailure)
        {
            return Result.Failure<WorkOrderDto>(result.Error);
        }

        // 报工记录是新实体，必须显式 Add
        repository.AddReport(workOrder.CreateReport(
            operationTaskId,
            request.GoodQuantity,
            request.DefectQuantity,
            request.ScrapQuantity,
            request.DefectCode,
            currentUser.UserId,
            request.Remark));

        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        EnqueueNotification(dto, "reported");
        return Result.Success(dto);
    }

    public async Task<Result<WorkOrderDto>> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
        => await TransitionAsync(id, "completed", w => w.Complete(), cancellationToken);

    public async Task<Result<WorkOrderDto>> CancelAsync(Guid id, CancellationToken cancellationToken = default)
        => await TransitionAsync(id, "cancelled", w => w.Cancel(), cancellationToken);

    private async Task<Result<WorkOrderDto>> TransitionAsync(
        Guid id,
        string action,
        Func<WorkOrder, Result> transition,
        CancellationToken cancellationToken)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return NotFound();
        }

        var result = transition(workOrder);
        if (result.IsFailure)
        {
            return Result.Failure<WorkOrderDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        EnqueueNotification(dto, action);
        return Result.Success(dto);
    }

    private static RoutingReleaseSnapshot ToReleaseSnapshot(RoutingSnapshot routing, BomSnapshot? bom)
        => new(
            routing.Id,
            routing.Version,
            bom?.Id,
            bom?.Version,
            routing.Steps
                .Select(step => new RoutingStepReleaseSnapshot(
                    step.Sequence,
                    step.OperationId,
                    step.OperationCode,
                    step.OperationName,
                    step.WorkCenterId,
                    step.StandardSeconds,
                    step.IsQualityGate))
                .ToList());

    /// <summary>实时通知延迟到事务提交成功之后执行，避免「看板已刷新、但数据被回滚」。</summary>
    private void EnqueueNotification(WorkOrderDto workOrder, string action)
        => postCommit.Enqueue(token => notifier.NotifyWorkOrderChangedAsync(workOrder, action, token));

    private static Result<WorkOrderDto> NotFound()
        => Result.Failure<WorkOrderDto>(Error.NotFound("WorkOrder.NotFound", "工单不存在"));

    private static WorkOrderDto ToDto(WorkOrder w) => new(
        w.Id,
        w.OrderNumber,
        w.ProductId,
        w.ProductCode,
        w.ProductName,
        w.PlannedQuantity,
        w.CompletedQuantity,
        w.Status,
        w.PlannedStart,
        w.PlannedEnd,
        w.WorkCenter,
        w.Remark,
        w.RoutingId,
        w.RoutingVersion,
        w.BomId,
        w.BomVersion,
        w.OrderedOperations.Select(ToOperationDto).ToList(),
        w.CreatedAt,
        w.CompletedAt);

    private static WorkOrderOperationDto ToOperationDto(WorkOrderOperation o) => new(
        o.Id,
        o.Sequence,
        o.OperationId,
        o.OperationCode,
        o.OperationName,
        o.WorkCenterId,
        o.StandardSeconds,
        o.IsQualityGate,
        o.PlannedQuantity,
        o.GoodQuantity,
        o.DefectQuantity,
        o.ScrapQuantity,
        o.ReportedQuantity,
        o.ProgressPercent,
        o.Status,
        o.StartedAt,
        o.CompletedAt);
}
