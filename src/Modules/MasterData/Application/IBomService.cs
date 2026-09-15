using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

/// <summary>
/// BOM 服务。版本化规则：同一产品下版本唯一，且同时只有一个生效版本。
/// </summary>
public interface IBomService
{
    Task<Result<PagedResult<BomDto>>> GetListAsync(BomQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<BomDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<BomDto>> CreateAsync(CreateBomRequest request, CancellationToken cancellationToken = default);

    /// <summary>更新备注并整体替换明细行（生效版本也可改，改动只影响后续下达的工单）。</summary>
    Task<Result<BomDto>> UpdateAsync(Guid id, UpdateBomRequest request, CancellationToken cancellationToken = default);

    /// <summary>设为生效版本（同产品其它版本自动失效）。</summary>
    Task<Result<BomDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>删除（生效版本不允许删除）。</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 工艺路线服务。
/// </summary>
public interface IRoutingService
{
    Task<Result<PagedResult<RoutingDto>>> GetListAsync(RoutingQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<RoutingDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<RoutingDto>> CreateAsync(CreateRoutingRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoutingDto>> UpdateAsync(Guid id, UpdateRoutingRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoutingDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
