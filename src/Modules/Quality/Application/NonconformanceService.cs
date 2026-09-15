using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Application;

/// <summary>
/// 不合格品处置（NCR）服务：决定处置方式 → 返工/返修 → 复检 → 关闭。
/// </summary>
public interface INonconformanceService
{
    Task<Result<PagedResult<NonconformanceDto>>> GetListAsync(NonconformanceQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<NonconformanceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<NonconformanceDto>> CreateAsync(CreateNonconformanceRequest request, CancellationToken cancellationToken = default);

    /// <summary>决定处置方式（返工/返修进入处理中；让步接收/报废/退货直接关闭）。</summary>
    Task<Result<NonconformanceDto>> DecideAsync(Guid id, DecideNonconformanceRequest request, CancellationToken cancellationToken = default);

    /// <summary>登记一次维修 / 返工。</summary>
    Task<Result<NonconformanceDto>> StartRepairAsync(Guid id, StartRepairRequest request, CancellationToken cancellationToken = default);

    /// <summary>完成维修（需复检则转待复检，否则关闭）。</summary>
    Task<Result<NonconformanceDto>> CompleteRepairAsync(Guid id, CompleteRepairRequest request, CancellationToken cancellationToken = default);

    /// <summary>复检结果：合格则关闭，不合格则回到处理中。</summary>
    Task<Result<NonconformanceDto>> ReinspectAsync(Guid id, ReinspectResultRequest request, CancellationToken cancellationToken = default);

    /// <summary>维修后仍不合格时的兜底处置：报废关闭。</summary>
    Task<Result<NonconformanceDto>> ScrapAsync(Guid id, string? remark, CancellationToken cancellationToken = default);
}

public class NonconformanceService(
    INonconformanceRepository repository,
    ICurrentUser currentUser) : INonconformanceService
{
    public async Task<Result<PagedResult<NonconformanceDto>>> GetListAsync(
        NonconformanceQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new NonconformanceQuery
        {
            Status = request.Status,
            Disposition = request.Disposition,
            Keyword = request.Keyword,
            WorkOrderId = request.WorkOrderId,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        return Result.Success(new PagedResult<NonconformanceDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<NonconformanceDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ncr = await repository.GetByIdAsync(id, cancellationToken);
        return ncr is null ? NotFound() : Result.Success(ToDto(ncr));
    }

    public async Task<Result<NonconformanceDto>> CreateAsync(
        CreateNonconformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return Result.Failure<NonconformanceDto>(Error.Validation("Nonconformance.InvalidQuantity", "不良数量必须大于 0"));
        }

        var number = await repository.NextNumberAsync(DateTime.Now, cancellationToken);

        var ncr = new Nonconformance(
            number,
            request.Quantity,
            request.InspectionId,
            request.WorkOrderId,
            request.Sn,
            request.DefectCode,
            request.DefectDescription,
            request.ProductCode);

        repository.Add(ncr);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(ncr));
    }

    public async Task<Result<NonconformanceDto>> DecideAsync(
        Guid id,
        DecideNonconformanceRequest request,
        CancellationToken cancellationToken = default)
    {
        var ncr = await repository.GetByIdAsync(id, cancellationToken);
        if (ncr is null)
        {
            return NotFound();
        }

        var result = ncr.Decide(request.Disposition, request.NeedReinspect, currentUser.UserId, request.Remark);
        if (result.IsFailure)
        {
            return Result.Failure<NonconformanceDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(ncr));
    }

    public async Task<Result<NonconformanceDto>> StartRepairAsync(
        Guid id,
        StartRepairRequest request,
        CancellationToken cancellationToken = default)
    {
        var ncr = await repository.GetByIdAsync(id, cancellationToken);
        if (ncr is null)
        {
            return NotFound();
        }

        var result = ncr.StartRepair(request.Description, currentUser.UserId, request.Remark);
        if (result.IsFailure)
        {
            return Result.Failure<NonconformanceDto>(result.Error);
        }

        // 新子实体必须显式持久化
        repository.AddRepair(result.Value);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(ncr));
    }

    public async Task<Result<NonconformanceDto>> CompleteRepairAsync(
        Guid id,
        CompleteRepairRequest request,
        CancellationToken cancellationToken = default)
    {
        var ncr = await repository.GetByIdAsync(id, cancellationToken);
        if (ncr is null)
        {
            return NotFound();
        }

        var result = ncr.CompleteRepair(request.RepairId, request.Result, request.Remark);
        if (result.IsFailure)
        {
            return Result.Failure<NonconformanceDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(ncr));
    }

    public async Task<Result<NonconformanceDto>> ReinspectAsync(
        Guid id,
        ReinspectResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var ncr = await repository.GetByIdAsync(id, cancellationToken);
        if (ncr is null)
        {
            return NotFound();
        }

        var result = request.Passed
            ? ncr.PassReinspection(request.ReinspectionId, request.Remark)
            : ncr.FailReinspection(request.ReinspectionId, request.Remark);

        if (result.IsFailure)
        {
            return Result.Failure<NonconformanceDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(ncr));
    }

    public async Task<Result<NonconformanceDto>> ScrapAsync(
        Guid id,
        string? remark,
        CancellationToken cancellationToken = default)
    {
        var ncr = await repository.GetByIdAsync(id, cancellationToken);
        if (ncr is null)
        {
            return NotFound();
        }

        var result = ncr.Scrap(remark);
        if (result.IsFailure)
        {
            return Result.Failure<NonconformanceDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(ncr));
    }

    private static Result<NonconformanceDto> NotFound()
        => Result.Failure<NonconformanceDto>(Error.NotFound("Nonconformance.NotFound", "处置单不存在"));

    private static NonconformanceDto ToDto(Nonconformance ncr) => new(
        ncr.Id,
        ncr.NonconformanceNumber,
        ncr.InspectionId,
        ncr.WorkOrderId,
        ncr.Sn,
        ncr.ProductCode,
        ncr.DefectCode,
        ncr.DefectDescription,
        ncr.Quantity,
        ncr.Disposition,
        ncr.Status,
        ncr.NeedReinspect,
        ncr.HandlerId,
        ncr.ReinspectionId,
        ncr.CreatedAt,
        ncr.DecidedAt,
        ncr.ClosedAt,
        ncr.Remark,
        ncr.OrderedRepairs
            .Select(r => new RepairRecordDto(r.Id, r.Description, r.Result, r.RepairerId, r.StartedAt, r.CompletedAt, r.Remark))
            .ToList());
}
