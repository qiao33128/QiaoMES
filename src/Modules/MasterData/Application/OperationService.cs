using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

public class OperationService(ICatalogRepository repository)
    : CatalogServiceBase<Operation, OperationDto, CreateOperationRequest, UpdateOperationRequest>(repository), IOperationService
{
    protected override string EntityKey => "Operation";

    protected override string EntityName => "工序";

    protected override string GetCode(CreateOperationRequest request) => request.Code;

    protected override Error? ValidateCreate(CreateOperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Error.Validation("Operation.InvalidCode", "工序编码不能为空");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("Operation.InvalidName", "工序名称不能为空");
        }
        if (request.StandardSeconds < 0)
        {
            return Error.Validation("Operation.InvalidStandardTime", "标准工时不能为负数");
        }

        return null;
    }

    protected override Operation CreateEntity(CreateOperationRequest request)
        => new(request.Code, request.Name, request.StandardSeconds, request.IsKeyOperation,
            request.DefaultWorkCenterId, request.Remark);

    protected override Error? ApplyUpdate(Operation entity, UpdateOperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("Operation.InvalidName", "工序名称不能为空");
        }
        if (request.StandardSeconds < 0)
        {
            return Error.Validation("Operation.InvalidStandardTime", "标准工时不能为负数");
        }

        entity.UpdateBasicInfo(request.Name, null, null, request.Remark);
        entity.UpdateProcessInfo(request.StandardSeconds, request.IsKeyOperation, request.DefaultWorkCenterId);
        return null;
    }

    protected override OperationDto ToDto(Operation entity)
        => new(entity.Id, entity.Code, entity.Name, entity.StandardSeconds, entity.IsKeyOperation,
            entity.DefaultWorkCenterId, entity.Remark, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
}
