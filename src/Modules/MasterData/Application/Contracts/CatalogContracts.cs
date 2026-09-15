using QiaoMES.MasterData.Domain;

namespace QiaoMES.MasterData.Application.Contracts;

/// <summary>主数据通用查询请求。</summary>
public record CatalogQueryRequest(int Page = 1, int PageSize = 20, string? Keyword = null, bool? IsActive = null);

/// <summary>启用 / 停用请求。</summary>
public record SetActiveRequest(bool IsActive);

/// <summary>CSV 导入请求（正文直接是 CSV 文本）。</summary>
public record ImportCatalogCsvRequest(string Content);

// ---------------- 产品 ----------------

public record ProductDto(
    Guid Id, string Code, string Name, string? Spec, string? Unit, string? Remark,
    bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public record CreateProductRequest(string Code, string Name, string? Spec, string? Unit, string? Remark);

public record UpdateProductRequest(string Name, string? Spec, string? Unit, string? Remark);

// ---------------- 物料 ----------------

public record MaterialDto(
    Guid Id, string Code, string Name, MaterialType MaterialType, string? SupplierPartNumber,
    string? Spec, string? Unit, string? Remark, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public record CreateMaterialRequest(
    string Code, string Name, MaterialType MaterialType,
    string? SupplierPartNumber, string? Spec, string? Unit, string? Remark);

public record UpdateMaterialRequest(
    string Name, MaterialType MaterialType,
    string? SupplierPartNumber, string? Spec, string? Unit, string? Remark);

// ---------------- 工作中心 ----------------

public record WorkCenterDto(
    Guid Id, string Code, string Name, WorkCenterType Type, string? Workshop, Guid? ParentId,
    string? Remark, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public record CreateWorkCenterRequest(
    string Code, string Name, WorkCenterType Type, string? Workshop, Guid? ParentId, string? Remark);

public record UpdateWorkCenterRequest(
    string Name, WorkCenterType Type, string? Workshop, Guid? ParentId, string? Remark);

// ---------------- 工序 ----------------

public record OperationDto(
    Guid Id, string Code, string Name, int StandardSeconds, bool IsKeyOperation,
    Guid? DefaultWorkCenterId, string? Remark, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

public record CreateOperationRequest(
    string Code, string Name, int StandardSeconds, bool IsKeyOperation,
    Guid? DefaultWorkCenterId, string? Remark);

public record UpdateOperationRequest(
    string Name, int StandardSeconds, bool IsKeyOperation,
    Guid? DefaultWorkCenterId, string? Remark);
