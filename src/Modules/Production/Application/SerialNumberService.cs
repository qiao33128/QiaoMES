using QiaoMES.Production.Application.Contracts;
using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application;

public class SerialNumberService(
    ISerialNumberRepository serialNumberRepository,
    IWorkOrderRepository workOrderRepository,
    ICurrentUser currentUser) : ISerialNumberService
{
    private const int MaxGeneratePerRequest = 1000;

    public async Task<Result<IReadOnlyList<SerialNumberDto>>> GenerateAsync(
        GenerateSerialNumbersRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0 || request.Quantity > MaxGeneratePerRequest)
        {
            return Result.Failure<IReadOnlyList<SerialNumberDto>>(
                Error.Validation("SerialNumber.InvalidQuantity", $"单次生成数量必须在 1 ~ {MaxGeneratePerRequest} 之间"));
        }

        var workOrder = await workOrderRepository.GetByIdAsync(request.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<IReadOnlyList<SerialNumberDto>>(Error.NotFound("WorkOrder.NotFound", "工单不存在"));
        }
        if (workOrder.Status is WorkOrderStatus.Draft or WorkOrderStatus.Cancelled)
        {
            return Result.Failure<IReadOnlyList<SerialNumberDto>>(
                Error.Conflict("SerialNumber.OrderNotReleased", "工单尚未下达或已取消，不能生成 SN"));
        }

        var prefix = string.IsNullOrWhiteSpace(request.SnPrefix) ? workOrder.OrderNumber : request.SnPrefix.Trim();
        var start = await serialNumberRepository.CountByWorkOrderAsync(workOrder.Id, cancellationToken);
        var created = new List<SerialNumberDto>();

        for (var index = 0; index < request.Quantity; index++)
        {
            var sn = $"{prefix}-{start + index + 1:D4}";

            // 幂等：已存在的编号直接跳过
            if (await serialNumberRepository.ExistsAsync(sn, cancellationToken))
            {
                continue;
            }

            var entity = new SerialNumber(sn, workOrder.Id, workOrder.ProductId, workOrder.ProductCode);
            serialNumberRepository.Add(entity);
            created.Add(ToDto(entity, workOrder.OrderNumber));
        }

        await serialNumberRepository.SaveChangesAsync(cancellationToken);
        return Result.Success<IReadOnlyList<SerialNumberDto>>(created);
    }

    public async Task<Result<PagedResult<SerialNumberDto>>> GetListAsync(
        SerialNumberQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new SerialNumberQuery
        {
            WorkOrderId = request.WorkOrderId,
            Status = request.Status,
            Keyword = request.Keyword,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await serialNumberRepository.QueryAsync(query, cancellationToken);
        var orderNumbers = await LoadOrderNumbersAsync(items.Select(s => s.WorkOrderId), cancellationToken);

        return Result.Success(new PagedResult<SerialNumberDto>
        {
            Items = items.Select(s => ToDto(s, orderNumbers.GetValueOrDefault(s.WorkOrderId, string.Empty))).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<SerialNumberDetailDto>> GetBySnAsync(string sn, CancellationToken cancellationToken = default)
    {
        var entity = await serialNumberRepository.GetBySnAsync(sn.Trim(), cancellationToken);
        if (entity is null)
        {
            return Result.Failure<SerialNumberDetailDto>(Error.NotFound("SerialNumber.NotFound", $"SN {sn} 不存在"));
        }

        var workOrder = await workOrderRepository.GetByIdAsync(entity.WorkOrderId, cancellationToken);
        return Result.Success(ToDetailDto(entity, workOrder?.OrderNumber ?? string.Empty));
    }

    public async Task<Result<SerialNumberDetailDto>> TrackInAsync(
        string sn,
        SnTrackInRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await serialNumberRepository.GetBySnAsync(sn.Trim(), cancellationToken);
        if (entity is null)
        {
            return Result.Failure<SerialNumberDetailDto>(Error.NotFound("SerialNumber.NotFound", $"SN {sn} 不存在"));
        }

        var workOrder = await workOrderRepository.GetByIdAsync(entity.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<SerialNumberDetailDto>(Error.NotFound("WorkOrder.NotFound", "SN 关联的工单不存在"));
        }

        var operation = workOrder.OrderedOperations.FirstOrDefault(o => o.Id == request.OperationTaskId);
        if (operation is null)
        {
            return Result.Failure<SerialNumberDetailDto>(
                Error.Validation("SerialNumber.OperationNotFound", "工序任务不属于该 SN 的工单"));
        }

        var result = entity.TrackIn(operation.Id, operation.OperationName, currentUser.UserId, request.EquipmentId, request.Remark);
        if (result.IsFailure)
        {
            return Result.Failure<SerialNumberDetailDto>(result.Error);
        }

        serialNumberRepository.AddTracking(result.Value);
        await serialNumberRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetailDto(entity, workOrder.OrderNumber));
    }

    public async Task<Result<SerialNumberDetailDto>> TrackOutAsync(
        string sn,
        SnTrackOutRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await serialNumberRepository.GetBySnAsync(sn.Trim(), cancellationToken);
        if (entity is null)
        {
            return Result.Failure<SerialNumberDetailDto>(Error.NotFound("SerialNumber.NotFound", $"SN {sn} 不存在"));
        }

        var workOrder = await workOrderRepository.GetByIdAsync(entity.WorkOrderId, cancellationToken);
        if (workOrder is null)
        {
            return Result.Failure<SerialNumberDetailDto>(Error.NotFound("WorkOrder.NotFound", "SN 关联的工单不存在"));
        }

        var operations = workOrder.OrderedOperations;
        var operation = operations.FirstOrDefault(o => o.Id == request.OperationTaskId);
        if (operation is null)
        {
            return Result.Failure<SerialNumberDetailDto>(
                Error.Validation("SerialNumber.OperationNotFound", "工序任务不属于该 SN 的工单"));
        }

        var isLastOperation = operations.Count > 0 && operations[^1].Id == operation.Id;

        var result = entity.TrackOut(
            operation.Id,
            operation.OperationName,
            request.Result,
            isLastOperation,
            currentUser.UserId,
            request.EquipmentId,
            request.Remark);

        if (result.IsFailure)
        {
            return Result.Failure<SerialNumberDetailDto>(result.Error);
        }

        serialNumberRepository.AddTracking(result.Value);
        await serialNumberRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDetailDto(entity, workOrder.OrderNumber));
    }

    public async Task<Result<SerialNumberDetailDto>> ScrapAsync(
        string sn,
        string? remark = null,
        CancellationToken cancellationToken = default)
    {
        var entity = await serialNumberRepository.GetBySnAsync(sn.Trim(), cancellationToken);
        if (entity is null)
        {
            return Result.Failure<SerialNumberDetailDto>(Error.NotFound("SerialNumber.NotFound", $"SN {sn} 不存在"));
        }

        var result = entity.Scrap(remark);
        if (result.IsFailure)
        {
            return Result.Failure<SerialNumberDetailDto>(result.Error);
        }

        await serialNumberRepository.SaveChangesAsync(cancellationToken);

        var workOrder = await workOrderRepository.GetByIdAsync(entity.WorkOrderId, cancellationToken);
        return Result.Success(ToDetailDto(entity, workOrder?.OrderNumber ?? string.Empty));
    }

    private async Task<Dictionary<Guid, string>> LoadOrderNumbersAsync(
        IEnumerable<Guid> workOrderIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, string>();

        foreach (var id in workOrderIds.Distinct())
        {
            var workOrder = await workOrderRepository.GetByIdAsync(id, cancellationToken);
            if (workOrder is not null)
            {
                result[id] = workOrder.OrderNumber;
            }
        }

        return result;
    }

    private static SerialNumberDto ToDto(SerialNumber entity, string orderNumber) => new(
        entity.Id,
        entity.Sn,
        entity.WorkOrderId,
        orderNumber,
        entity.ProductId,
        entity.ProductCode,
        entity.CurrentOperationTaskId,
        entity.CurrentOperationName,
        entity.LastCompletedOperationName,
        entity.Status,
        entity.CreatedAt,
        entity.CompletedAt);

    private static SerialNumberDetailDto ToDetailDto(SerialNumber entity, string orderNumber) => new(
        ToDto(entity, orderNumber),
        entity.Trackings
            .OrderBy(t => t.TrackedAt)
            .Select(t => new WipTrackingDto(
                t.Id,
                t.WorkOrderOperationId,
                t.OperationName,
                t.Action,
                t.Result,
                t.OperatorId,
                t.EquipmentId,
                t.Remark,
                t.TrackedAt))
            .ToList());
}
