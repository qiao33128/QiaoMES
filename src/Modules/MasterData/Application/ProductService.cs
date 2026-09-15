using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

public class ProductService(ICatalogRepository repository)
    : CatalogServiceBase<Product, ProductDto, CreateProductRequest, UpdateProductRequest>(repository), IProductService
{
    protected override string EntityKey => "Product";

    protected override string EntityName => "产品";

    protected override string GetCode(CreateProductRequest request) => request.Code;

    protected override Error? ValidateCreate(CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Error.Validation("Product.InvalidCode", "产品编码不能为空");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("Product.InvalidName", "产品名称不能为空");
        }

        return null;
    }

    protected override Product CreateEntity(CreateProductRequest request)
        => new(request.Code, request.Name, request.Spec, request.Unit, request.Remark);

    protected override Error? ApplyUpdate(Product entity, UpdateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("Product.InvalidName", "产品名称不能为空");
        }

        entity.UpdateBasicInfo(request.Name, request.Spec, request.Unit, request.Remark);
        return null;
    }

    protected override ProductDto ToDto(Product entity)
        => new(entity.Id, entity.Code, entity.Name, entity.Spec, entity.Unit, entity.Remark,
            entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
}
