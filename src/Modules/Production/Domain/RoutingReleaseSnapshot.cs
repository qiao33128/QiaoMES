namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单下达时使用的工艺路线快照。
/// <para>
/// 这是生产模块自己的契约类型：由应用层从 MasterData 的只读快照转换而来，
/// 领域层不依赖主数据模块。
/// </para>
/// </summary>
public sealed record RoutingReleaseSnapshot(
    Guid RoutingId,
    string Version,
    Guid? BomId,
    string? BomVersion,
    IReadOnlyList<RoutingStepReleaseSnapshot> Steps);

/// <summary>工艺路线中的一步（下达快照）。</summary>
public sealed record RoutingStepReleaseSnapshot(
    int Sequence,
    Guid OperationId,
    string OperationCode,
    string OperationName,
    Guid? WorkCenterId,
    int StandardSeconds,
    bool IsQualityGate);
