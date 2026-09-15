using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Application;

/// <summary>
/// 不良代码主数据服务。
/// </summary>
public interface IDefectCodeService
{
    Task<Result<PagedResult<DefectCodeDto>>> GetListAsync(DefectCodeQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<DefectCodeDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<DefectCodeDto>> CreateAsync(CreateDefectCodeRequest request, CancellationToken cancellationToken = default);

    Task<Result<DefectCodeDto>> UpdateAsync(Guid id, UpdateDefectCodeRequest request, CancellationToken cancellationToken = default);

    Task<Result<DefectCodeDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>不良 Pareto：按不良代码统计出现次数（默认最近 30 天）。</summary>
    Task<Result<IReadOnlyList<DefectParetoDto>>> GetParetoAsync(
        DateTime? from = null,
        DateTime? to = null,
        int top = 10,
        CancellationToken cancellationToken = default);
}

public class DefectCodeService(IDefectCodeRepository repository) : IDefectCodeService
{
    public async Task<Result<PagedResult<DefectCodeDto>>> GetListAsync(
        DefectCodeQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new DefectCodeQuery
        {
            Keyword = request.Keyword,
            Category = request.Category,
            IsActive = request.IsActive,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        return Result.Success(new PagedResult<DefectCodeDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<DefectCodeDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        return entity is null ? NotFound() : Result.Success(ToDto(entity));
    }

    public async Task<Result<DefectCodeDto>> CreateAsync(
        CreateDefectCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<DefectCodeDto>(Error.Validation("DefectCode.InvalidCode", "不良代码不能为空"));
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<DefectCodeDto>(Error.Validation("DefectCode.InvalidName", "不良名称不能为空"));
        }

        var code = request.Code.Trim();
        if (await repository.IsCodeTakenAsync(code, null, cancellationToken))
        {
            return Result.Failure<DefectCodeDto>(Error.Conflict("DefectCode.CodeTaken", $"不良代码 {code} 已存在"));
        }

        var entity = new DefectCode(code, request.Name, request.Category, request.Description);
        repository.Add(entity);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<DefectCodeDto>> UpdateAsync(
        Guid id,
        UpdateDefectCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<DefectCodeDto>(Error.Validation("DefectCode.InvalidName", "不良名称不能为空"));
        }

        entity.Update(request.Name, request.Category, request.Description);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<DefectCodeDto>> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var entity = await repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        entity.SetActive(isActive);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<IReadOnlyList<DefectParetoDto>>> GetParetoAsync(
        DateTime? from = null,
        DateTime? to = null,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;
        var take = top is < 1 or > 50 ? 10 : top;

        var result = await repository.TopDefectsAsync(start, end, take, cancellationToken);
        return Result.Success<IReadOnlyList<DefectParetoDto>>(
            result.Select(x => new DefectParetoDto(x.DefectCode, x.Count)).ToList());
    }

    private static Result<DefectCodeDto> NotFound()
        => Result.Failure<DefectCodeDto>(Error.NotFound("DefectCode.NotFound", "不良代码不存在"));

    private static DefectCodeDto ToDto(DefectCode entity) => new(
        entity.Id,
        entity.Code,
        entity.Name,
        entity.Category,
        entity.Description,
        entity.IsActive,
        entity.CreatedAt,
        entity.UpdatedAt);
}
