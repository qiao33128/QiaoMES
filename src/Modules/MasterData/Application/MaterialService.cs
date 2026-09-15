using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

public class MaterialService(ICatalogRepository repository)
    : CatalogServiceBase<Material, MaterialDto, CreateMaterialRequest, UpdateMaterialRequest>(repository), IMaterialService
{
    protected override string EntityKey => "Material";

    protected override string EntityName => "物料";

    protected override string GetCode(CreateMaterialRequest request) => request.Code;

    protected override Error? ValidateCreate(CreateMaterialRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Error.Validation("Material.InvalidCode", "物料编码不能为空");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("Material.InvalidName", "物料名称不能为空");
        }

        return null;
    }

    protected override Material CreateEntity(CreateMaterialRequest request)
    {
        var material = new Material(request.Code, request.Name, request.MaterialType, request.Spec, request.Unit, request.Remark);
        material.UpdateMaterialInfo(request.MaterialType, request.SupplierPartNumber);
        return material;
    }

    protected override Error? ApplyUpdate(Material entity, UpdateMaterialRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("Material.InvalidName", "物料名称不能为空");
        }

        entity.UpdateBasicInfo(request.Name, request.Spec, request.Unit, request.Remark);
        entity.UpdateMaterialInfo(request.MaterialType, request.SupplierPartNumber);
        return null;
    }

    protected override MaterialDto ToDto(Material entity)
        => new(entity.Id, entity.Code, entity.Name, entity.MaterialType, entity.SupplierPartNumber,
            entity.Spec, entity.Unit, entity.Remark, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
}
