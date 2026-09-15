namespace QiaoMES.MasterData.Application.Contracts;

/// <summary>工艺路线查询请求。</summary>
public record RoutingQueryRequest(
    Guid? ProductId = null,
    bool? IsActive = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>工艺路线中的一个工序步骤。</summary>
public record RoutingStepDto(
    Guid Id,
    int Sequence,
    Guid OperationId,
    string OperationCode,
    string OperationName,
    Guid? WorkCenterId,
    string? WorkCenterCode,
    string? WorkCenterName,
    int StandardSeconds,
    bool IsQualityGate);

/// <summary>工艺路线主信息。</summary>
public record RoutingDto(
    Guid Id,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Version,
    bool IsActive,
    string? Remark,
    int StepCount,
    int TotalStandardSeconds,
    IReadOnlyList<RoutingStepDto> Steps,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>工序步骤输入。</summary>
public record RoutingStepRequest(
    int Sequence,
    Guid OperationId,
    Guid? WorkCenterId,
    int StandardSeconds,
    bool IsQualityGate = false);

public record CreateRoutingRequest(
    Guid ProductId,
    string Version,
    string? Remark,
    IReadOnlyList<RoutingStepRequest> Steps);

public record UpdateRoutingRequest(
    string? Remark,
    IReadOnlyList<RoutingStepRequest> Steps);
