namespace QiaoMES.MasterData.Application.Contracts;

/// <summary>BOM 查询请求。</summary>
public record BomQueryRequest(
    Guid? ProductId = null,
    bool? IsActive = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>BOM 明细行。</summary>
public record BomItemDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    decimal Quantity,
    string? Unit,
    decimal LossRate,
    decimal RequiredQuantity,
    string? Remark);

/// <summary>BOM 主信息。</summary>
public record BomDto(
    Guid Id,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Version,
    bool IsActive,
    string? Remark,
    int ItemCount,
    IReadOnlyList<BomItemDto> Items,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

/// <summary>BOM 明细行输入。</summary>
public record BomItemRequest(
    Guid MaterialId,
    decimal Quantity,
    string? Unit,
    decimal LossRate = 0,
    string? Remark = null);

public record CreateBomRequest(
    Guid ProductId,
    string Version,
    string? Remark,
    IReadOnlyList<BomItemRequest> Items);

public record UpdateBomRequest(
    string? Remark,
    IReadOnlyList<BomItemRequest> Items);
