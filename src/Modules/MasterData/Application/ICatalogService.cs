using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

/// <summary>
/// 主数据通用服务契约：查询、创建、更新、启停。
/// </summary>
public interface ICatalogService<TDto, TCreateRequest, TUpdateRequest>
{
    Task<Result<PagedResult<TDto>>> GetListAsync(CatalogQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<TDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<TDto>> CreateAsync(TCreateRequest request, CancellationToken cancellationToken = default);

    Task<Result<TDto>> UpdateAsync(Guid id, TUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>启用 / 停用（停用后不允许被新工单、新工艺路线引用）。</summary>
    Task<Result<TDto>> SetActiveAsync(Guid id, SetActiveRequest request, CancellationToken cancellationToken = default);
}

/// <summary>产品服务。</summary>
public interface IProductService : ICatalogService<ProductDto, CreateProductRequest, UpdateProductRequest>;

/// <summary>物料服务。</summary>
public interface IMaterialService : ICatalogService<MaterialDto, CreateMaterialRequest, UpdateMaterialRequest>;

/// <summary>工作中心服务。</summary>
public interface IWorkCenterService : ICatalogService<WorkCenterDto, CreateWorkCenterRequest, UpdateWorkCenterRequest>;

/// <summary>工序服务。</summary>
public interface IOperationService : ICatalogService<OperationDto, CreateOperationRequest, UpdateOperationRequest>;
