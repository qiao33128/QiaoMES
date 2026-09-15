using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

/// <summary>
/// 主数据服务的公共实现：把「编码唯一校验 + 分页查询 + 启停」这些完全一致的逻辑收敛在一处，
/// 子类只负责字段映射与校验。
/// </summary>
public abstract class CatalogServiceBase<TEntity, TDto, TCreateRequest, TUpdateRequest>(ICatalogRepository repository)
    : ICatalogService<TDto, TCreateRequest, TUpdateRequest>
    where TEntity : CatalogEntity
{
    /// <summary>英文实体键，用于错误码（如 <c>Product</c>）。</summary>
    protected abstract string EntityKey { get; }

    /// <summary>中文实体名，用于错误描述（如「产品」）。</summary>
    protected abstract string EntityName { get; }

    protected abstract string GetCode(TCreateRequest request);

    protected abstract Error? ValidateCreate(TCreateRequest request);

    protected abstract TEntity CreateEntity(TCreateRequest request);

    protected abstract Error? ApplyUpdate(TEntity entity, TUpdateRequest request);

    protected abstract TDto ToDto(TEntity entity);

    public async Task<Result<PagedResult<TDto>>> GetListAsync(
        CatalogQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new CatalogQuery
        {
            Keyword = request.Keyword,
            IsActive = request.IsActive,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryPagedAsync<TEntity>(query, cancellationToken);

        return Result.Success(new PagedResult<TDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<TDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await repository.GetByIdAsync<TEntity>(id, cancellationToken);
        return entity is null ? NotFound() : Result.Success(ToDto(entity));
    }

    public async Task<Result<TDto>> CreateAsync(TCreateRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateCreate(request);
        if (validationError is not null)
        {
            return Result.Failure<TDto>(validationError);
        }

        var code = GetCode(request).Trim();
        if (await repository.IsCodeTakenAsync<TEntity>(code, null, cancellationToken))
        {
            return Result.Failure<TDto>(Error.Conflict($"{EntityKey}.CodeTaken", $"{EntityName}编码 {code} 已存在"));
        }

        var entity = CreateEntity(request);
        repository.Add(entity);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(entity));
    }

    public async Task<Result<TDto>> UpdateAsync(Guid id, TUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await repository.GetByIdAsync<TEntity>(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var validationError = ApplyUpdate(entity, request);
        if (validationError is not null)
        {
            return Result.Failure<TDto>(validationError);
        }

        // 实体处于变更跟踪中，直接保存即可
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    public async Task<Result<TDto>> SetActiveAsync(Guid id, SetActiveRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await repository.GetByIdAsync<TEntity>(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        entity.SetActive(request.IsActive);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(entity));
    }

    private Result<TDto> NotFound()
        => Result.Failure<TDto>(Error.NotFound($"{EntityKey}.NotFound", $"{EntityName}不存在"));
}
