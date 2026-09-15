namespace QiaoMES.MasterData.Application;

/// <summary>产品快照（供其它模块只读使用，不含领域实体）。</summary>
public record ProductSnapshot(Guid Id, string Code, string Name, bool IsActive);

/// <summary>工艺路线步骤快照。</summary>
public record RoutingStepSnapshot(
    int Sequence,
    Guid OperationId,
    string OperationCode,
    string OperationName,
    Guid? WorkCenterId,
    int StandardSeconds,
    bool IsQualityGate);

/// <summary>工艺路线快照（含步骤）。</summary>
public record RoutingSnapshot(Guid Id, string Version, IReadOnlyList<RoutingStepSnapshot> Steps);

/// <summary>BOM 快照。</summary>
public record BomSnapshot(Guid Id, string Version, int ItemCount);

/// <summary>
/// 主数据只读查询契约。
/// <para>
/// 其它模块（如 Production）通过本接口获取产品、生效工艺路线与生效 BOM，
/// 只拿到快照 DTO，不接触主数据领域实体，保持模块边界清晰。
/// </para>
/// </summary>
public interface IMasterDataQueryService
{
    Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>取产品当前生效的工艺路线（不存在返回 <c>null</c>）。</summary>
    Task<RoutingSnapshot?> GetActiveRoutingAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>取产品当前生效的 BOM（不存在返回 <c>null</c>）。</summary>
    Task<BomSnapshot?> GetActiveBomAsync(Guid productId, CancellationToken cancellationToken = default);
}
