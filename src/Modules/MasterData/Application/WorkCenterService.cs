using QiaoMES.MasterData.Application.Contracts;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

public class WorkCenterService(ICatalogRepository repository)
    : CatalogServiceBase<WorkCenter, WorkCenterDto, CreateWorkCenterRequest, UpdateWorkCenterRequest>(repository), IWorkCenterService
{
    protected override string EntityKey => "WorkCenter";

    protected override string EntityName => "工作中心";

    protected override string GetCode(CreateWorkCenterRequest request) => request.Code;

    protected override Error? ValidateCreate(CreateWorkCenterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Error.Validation("WorkCenter.InvalidCode", "工作中心编码不能为空");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("WorkCenter.InvalidName", "工作中心名称不能为空");
        }

        return null;
    }

    protected override WorkCenter CreateEntity(CreateWorkCenterRequest request)
    {
        var workCenter = new WorkCenter(request.Code, request.Name, request.Type, request.Workshop, request.Remark);
        workCenter.UpdateLayout(request.Type, request.Workshop, request.ParentId);
        return workCenter;
    }

    protected override Error? ApplyUpdate(WorkCenter entity, UpdateWorkCenterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation("WorkCenter.InvalidName", "工作中心名称不能为空");
        }
        if (request.ParentId == entity.Id)
        {
            return Error.Validation("WorkCenter.InvalidParent", "上级工作中心不能是自己");
        }

        entity.UpdateBasicInfo(request.Name, null, null, request.Remark);
        entity.UpdateLayout(request.Type, request.Workshop, request.ParentId);
        return null;
    }

    protected override WorkCenterDto ToDto(WorkCenter entity)
        => new(entity.Id, entity.Code, entity.Name, entity.Type, entity.Workshop, entity.ParentId,
            entity.Remark, entity.IsActive, entity.CreatedAt, entity.UpdatedAt);
}
