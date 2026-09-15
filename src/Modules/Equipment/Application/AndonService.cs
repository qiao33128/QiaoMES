using QiaoMES.Equipment.Application.Contracts;
using QiaoMES.Equipment.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Equipment.Application;

/// <summary>
/// Andon 呼叫服务：一键呼叫 → 响应 → 解决 → 关闭；超时由后台任务自动升级。
/// </summary>
public interface IAndonService
{
    Task<Result<PagedResult<AndonCallDto>>> GetListAsync(AndonQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<AndonCallDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<AndonCallDto>> CreateAsync(CreateAndonCallRequest request, CancellationToken cancellationToken = default);

    /// <summary>响应呼叫。</summary>
    Task<Result<AndonCallDto>> RespondAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>解决呼叫。</summary>
    Task<Result<AndonCallDto>> ResolveAsync(Guid id, ResolveAndonRequest request, CancellationToken cancellationToken = default);

    /// <summary>关闭呼叫。</summary>
    Task<Result<AndonCallDto>> CloseAsync(Guid id, ResolveAndonRequest request, CancellationToken cancellationToken = default);
}

public class AndonService(
    IAndonRepository andonRepository,
    IEquipmentRepository equipmentRepository,
    IAndonNotifier notifier,
    IPostCommitActions postCommit,
    ICurrentUser currentUser) : IAndonService
{
    public async Task<Result<PagedResult<AndonCallDto>>> GetListAsync(
        AndonQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new AndonQuery
        {
            Status = request.Status,
            Type = request.Type,
            Level = request.Level,
            OnlyOpen = request.OnlyOpen,
            Keyword = request.Keyword,
            From = request.From,
            To = request.To,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await andonRepository.QueryAsync(query, cancellationToken);
        var now = DateTime.UtcNow;

        return Result.Success(new PagedResult<AndonCallDto>
        {
            Items = items.Select(call => ToDto(call, now)).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<AndonCallDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var call = await andonRepository.GetByIdAsync(id, cancellationToken);
        return call is null ? NotFound() : Result.Success(ToDto(call, DateTime.UtcNow));
    }

    public async Task<Result<AndonCallDto>> CreateAsync(
        CreateAndonCallRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Result.Failure<AndonCallDto>(Error.Validation("Andon.InvalidDescription", "呼叫描述不能为空"));
        }

        // 带设备时自动带出设备编码，便于看板显示
        var equipmentCode = request.EquipmentCode;
        if (request.EquipmentId is not null && string.IsNullOrWhiteSpace(equipmentCode))
        {
            var equipment = await equipmentRepository.GetByIdAsync(request.EquipmentId.Value, cancellationToken);
            equipmentCode = equipment?.Code;
        }

        var callNumber = await andonRepository.NextNumberAsync(DateTime.Now, cancellationToken);

        var call = new AndonCall(
            callNumber,
            request.Type,
            request.Description,
            request.Level,
            request.EquipmentId,
            equipmentCode,
            request.WorkCenterId,
            request.WorkCenterName,
            request.Sn,
            request.TimeoutMinutes,
            currentUser.UserId);

        andonRepository.Add(call);
        await andonRepository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(call, DateTime.UtcNow);
        // 通知延迟到事务提交成功后发送
        postCommit.Enqueue(token => notifier.NotifyCalledAsync(call, token));

        return Result.Success(dto);
    }

    public async Task<Result<AndonCallDto>> RespondAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var call = await andonRepository.GetByIdAsync(id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        var result = call.Respond(currentUser.UserId);
        if (result.IsFailure)
        {
            return Result.Failure<AndonCallDto>(result.Error);
        }

        await andonRepository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(call, DateTime.UtcNow);
        postCommit.Enqueue(token => notifier.NotifyRespondedAsync(call, token));

        return Result.Success(dto);
    }

    public async Task<Result<AndonCallDto>> ResolveAsync(
        Guid id,
        ResolveAndonRequest request,
        CancellationToken cancellationToken = default)
    {
        var call = await andonRepository.GetByIdAsync(id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        var result = call.Resolve(request.Resolution);
        if (result.IsFailure)
        {
            return Result.Failure<AndonCallDto>(result.Error);
        }

        await andonRepository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(call, DateTime.UtcNow);
        postCommit.Enqueue(token => notifier.NotifyResolvedAsync(call, token));

        return Result.Success(dto);
    }

    public async Task<Result<AndonCallDto>> CloseAsync(
        Guid id,
        ResolveAndonRequest request,
        CancellationToken cancellationToken = default)
    {
        var call = await andonRepository.GetByIdAsync(id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        var result = call.Close(request.Resolution);
        if (result.IsFailure)
        {
            return Result.Failure<AndonCallDto>(result.Error);
        }

        await andonRepository.SaveChangesAsync(cancellationToken);

        var dto = ToDto(call, DateTime.UtcNow);
        postCommit.Enqueue(token => notifier.NotifyResolvedAsync(call, token));

        return Result.Success(dto);
    }

    private static Result<AndonCallDto> NotFound()
        => Result.Failure<AndonCallDto>(Error.NotFound("Andon.NotFound", "Andon 呼叫不存在"));

    private static AndonCallDto ToDto(AndonCall call, DateTime now) => new(
        call.Id,
        call.CallNumber,
        call.Type,
        call.Level,
        call.Status,
        call.EquipmentId,
        call.EquipmentCode,
        call.WorkCenterId,
        call.WorkCenterName,
        call.Sn,
        call.Description,
        call.CallerId,
        call.CalledAt,
        call.TimeoutMinutes,
        call.RespondedAt,
        call.ResponderId,
        call.ResolvedAt,
        call.Resolution,
        call.Escalated,
        call.EscalatedAt,
        call.IsTimeout(now));
}
