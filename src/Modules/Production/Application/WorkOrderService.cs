using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application;

public class WorkOrderService(
    IWorkOrderRepository repository,
    IWorkOrderNumberGenerator numberGenerator,
    IWorkOrderNotifier notifier,
    IPostCommitActions postCommit) : IWorkOrderService
{
    public async Task<Result<WorkOrderDto>> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode))
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.InvalidInput", "产品编码不能为空"));
        }
        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.InvalidInput", "产品名称不能为空"));
        }
        if (request.PlannedQuantity <= 0)
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.InvalidQuantity", "计划数量必须大于 0"));
        }

        // 单号由数据库原子生成，并发安全
        var orderNumber = await numberGenerator.NextAsync(DateTime.Now, cancellationToken);
        var workOrder = new WorkOrder(
            orderNumber,
            request.ProductCode.Trim(),
            request.ProductName.Trim(),
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
        return workOrder is null
            ? Result.Failure<WorkOrderDto>(Error.NotFound("WorkOrder.NotFound", "工单不存在"))
            : Result.Success(ToDto(workOrder));
    }

    public async Task<Result<PagedResult<WorkOrderDto>>> GetListAsync(
        PaginationRequest pagination, WorkOrderStatus? status, string? keyword, CancellationToken cancellationToken = default)
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

    public async Task<Result<WorkOrderDto>> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDto>(Error.NotFound("WorkOrder.NotFound", "工单不存在"));
        }
        if (workOrder.Status != WorkOrderStatus.Draft)
        {
            return Result.Failure<WorkOrderDto>(Error.Conflict("WorkOrder.NotEditable", "只有草稿状态的工单才能编辑"));
        }
        if (request.PlannedQuantity <= 0)
        {
            return Result.Failure<WorkOrderDto>(Error.Validation("WorkOrder.InvalidQuantity", "计划数量必须大于 0"));
        }

        workOrder.UpdatePlan(request.ProductName.Trim(), request.PlannedQuantity,
            request.PlannedStart, request.PlannedEnd, request.WorkCenter, request.Remark);
        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        EnqueueNotification(dto, "updated");
        return Result.Success(dto);
    }

    public async Task<Result<WorkOrderDto>> ReleaseAsync(Guid id, CancellationToken cancellationToken = default)
        => await TransitionAsync(id, "released", w => w.Release(), cancellationToken);

    public async Task<Result<WorkOrderDto>> StartProductionAsync(Guid id, CancellationToken cancellationToken = default)
        => await TransitionAsync(id, "started", w => w.StartProduction(), cancellationToken);

    public async Task<Result<WorkOrderDto>> ReportAsync(Guid id, ReportRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDto>(Error.NotFound("WorkOrder.NotFound", "工单不存在"));
        }

        var result = workOrder.Report(request.Quantity);
        if (result.IsFailure)
        {
            return Result.Failure<WorkOrderDto>(result.Error);
        }

        // 报工记录是新实体，必须显式 Add；工单本身已被跟踪，直接 SaveChanges 即可
        repository.AddReport(workOrder.CreateReport(request.Quantity));
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
        Guid id, string action, Func<WorkOrder, Result> transition, CancellationToken cancellationToken)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDto>(Error.NotFound("WorkOrder.NotFound", "工单不存在"));
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

    /// <summary>
    /// 实时通知延迟到事务提交成功之后执行，避免「看板已刷新、但数据被回滚」。
    /// </summary>
    private void EnqueueNotification(WorkOrderDto workOrder, string action)
        => postCommit.Enqueue(token => notifier.NotifyWorkOrderChangedAsync(workOrder, action, token));

    private static WorkOrderDto ToDto(WorkOrder w) => new(
        w.Id, w.OrderNumber, w.ProductCode, w.ProductName,
        w.PlannedQuantity, w.CompletedQuantity, w.Status,
        w.PlannedStart, w.PlannedEnd, w.WorkCenter, w.Remark,
        w.CreatedAt, w.CompletedAt);
}
