using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application;

public class WorkOrderService(
    IWorkOrderRepository repository,
    IWorkOrderNotifier notifier) : IWorkOrderService
{
    public async Task<Result<WorkOrderDto>> CreateAsync(CreateWorkOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode))
        {
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.InvalidInput", "产品编码不能为空"));
        }
        if (request.PlannedQuantity <= 0)
        {
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.InvalidQuantity", "计划数量必须大于 0"));
        }

        var orderNumber = await GenerateOrderNumberAsync(cancellationToken);
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
        await notifier.NotifyWorkOrderChangedAsync(dto, "created", cancellationToken);
        return Result.Success(dto);
    }

    public async Task<Result<WorkOrderDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        return workOrder is null
            ? Result.Failure<WorkOrderDto>(new Error("WorkOrder.NotFound", "工单不存在"))
            : Result.Success(ToDto(workOrder));
    }

    public async Task<Result<PagedResult<WorkOrderDto>>> GetListAsync(
        PaginationRequest pagination, WorkOrderStatus? status, string? keyword, CancellationToken cancellationToken = default)
    {
        var all = await repository.GetAllAsync(cancellationToken);

        IEnumerable<WorkOrder> query = all;
        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(w =>
                w.OrderNumber.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                w.ProductCode.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                w.ProductName.Contains(k, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = query.OrderByDescending(w => w.CreatedAt).ToList();
        var total = ordered.Count;
        var items = ordered
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(ToDto)
            .ToList();

        return Result.Success(new PagedResult<WorkOrderDto>
        {
            Items = items,
            Page = pagination.Page,
            PageSize = pagination.PageSize,
            TotalCount = total,
        });
    }

    public async Task<Result<WorkOrderDto>> UpdateAsync(Guid id, UpdateWorkOrderRequest request, CancellationToken cancellationToken = default)
    {
        var workOrder = await repository.GetByIdAsync(id, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.NotFound", "工单不存在"));
        }
        if (workOrder.Status != WorkOrderStatus.Draft)
        {
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.NotEditable", "只有草稿状态的工单才能编辑"));
        }
        if (request.PlannedQuantity <= 0)
        {
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.InvalidQuantity", "计划数量必须大于 0"));
        }

        workOrder.UpdatePlan(request.ProductName.Trim(), request.PlannedQuantity,
            request.PlannedStart, request.PlannedEnd, request.WorkCenter, request.Remark);
        repository.Update(workOrder);
        await repository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(workOrder);
        await notifier.NotifyWorkOrderChangedAsync(dto, "updated", cancellationToken);
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
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.NotFound", "工单不存在"));
        }

        var result = workOrder.Report(request.Quantity);
        if (result.IsFailure)
        {
            return Result.Failure<WorkOrderDto>(result.Error);
        }

        repository.AddReport(workOrder.CreateReport(request.Quantity));
        repository.Update(workOrder);
        await repository.SaveChangesAsync(cancellationToken);
        var dto = ToDto(workOrder);
        await notifier.NotifyWorkOrderChangedAsync(dto, "reported", cancellationToken);
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
            return Result.Failure<WorkOrderDto>(new Error("WorkOrder.NotFound", "工单不存在"));
        }

        var result = transition(workOrder);
        if (result.IsFailure)
        {
            return Result.Failure<WorkOrderDto>(result.Error);
        }

        repository.Update(workOrder);
        await repository.SaveChangesAsync(cancellationToken);
        var dto = ToDto(workOrder);
        await notifier.NotifyWorkOrderChangedAsync(dto, action, cancellationToken);
        return Result.Success(dto);
    }

    private async Task<string> GenerateOrderNumberAsync(CancellationToken cancellationToken)
    {
        var count = await repository.CountAsync(cancellationToken);
        var date = DateTime.Now.ToString("yyyyMMdd");
        // 预留查询次数避免并发冲突
        string orderNumber;
        do
        {
            orderNumber = $"WO-{date}-{count + 1:D4}";
            count++;
        }
        while (await repository.IsOrderNumberTakenAsync(orderNumber, cancellationToken));
        return orderNumber;
    }

    private static WorkOrderDto ToDto(WorkOrder w) => new(
        w.Id, w.OrderNumber, w.ProductCode, w.ProductName,
        w.PlannedQuantity, w.CompletedQuantity, w.Status,
        w.PlannedStart, w.PlannedEnd, w.WorkCenter, w.Remark,
        w.CreatedAt, w.CompletedAt);
}
