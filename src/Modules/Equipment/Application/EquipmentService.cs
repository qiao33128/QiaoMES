using QiaoMES.Equipment.Application.Contracts;
using QiaoMES.Equipment.Domain;
using QiaoMES.Shared;

// 设备实体名与模块根命名空间同名，用别名消除解析歧义
using EquipmentEntity = QiaoMES.Equipment.Domain.Equipment;

namespace QiaoMES.Equipment.Application;

/// <summary>
/// 设备台账与状态管理服务。
/// </summary>
public interface IEquipmentService
{
    Task<Result<PagedResult<EquipmentDto>>> GetListAsync(EquipmentQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<EquipmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<EquipmentDto>> CreateAsync(CreateEquipmentRequest request, CancellationToken cancellationToken = default);

    Task<Result<EquipmentDto>> UpdateAsync(Guid id, UpdateEquipmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>切换设备状态（运行/待机/故障/保养/离线），自动记录状态轨迹与停机时长。</summary>
    Task<Result<EquipmentDto>> ChangeStatusAsync(Guid id, ChangeEquipmentStatusRequest request, CancellationToken cancellationToken = default);

    Task<Result<EquipmentDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>登记点检 / 保养 / 维修记录。</summary>
    Task<Result<EquipmentDto>> AddMaintenanceAsync(Guid id, CreateMaintenanceRecordRequest request, CancellationToken cancellationToken = default);

    /// <summary>设备状态汇总（看板红黄绿）。</summary>
    Task<Result<EquipmentStatusSummaryDto>> GetStatusSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>停机原因 Pareto。</summary>
    Task<Result<IReadOnlyList<DowntimeParetoDto>>> GetDowntimeParetoAsync(
        DateTime? from = null,
        DateTime? to = null,
        int top = 10,
        CancellationToken cancellationToken = default);
}

public class EquipmentService(
    IEquipmentRepository repository,
    ICurrentUser currentUser) : IEquipmentService
{
    public async Task<Result<PagedResult<EquipmentDto>>> GetListAsync(
        EquipmentQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new EquipmentQuery
        {
            Keyword = request.Keyword,
            Status = request.Status,
            LineName = request.LineName,
            IsActive = request.IsActive,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        return Result.Success(new PagedResult<EquipmentDto>
        {
            // 列表不返回明细集合，减小响应体积
            Items = items.Select(e => ToDto(e, false)).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<EquipmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var equipment = await repository.GetByIdAsync(id, cancellationToken);
        return equipment is null ? NotFound() : Result.Success(ToDto(equipment, true));
    }

    public async Task<Result<EquipmentDto>> CreateAsync(
        CreateEquipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result.Failure<EquipmentDto>(Error.Validation("Equipment.InvalidCode", "设备编号不能为空"));
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<EquipmentDto>(Error.Validation("Equipment.InvalidName", "设备名称不能为空"));
        }

        var code = request.Code.Trim();
        if (await repository.IsCodeTakenAsync(code, null, cancellationToken))
        {
            return Result.Failure<EquipmentDto>(Error.Conflict("Equipment.CodeTaken", $"设备编号 {code} 已存在"));
        }

        var equipment = new EquipmentEntity(
            code,
            request.Name,
            request.Model,
            request.SerialNumber,
            request.WorkCenterId,
            request.LineName,
            request.Remark);

        repository.Add(equipment);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(equipment, true));
    }

    public async Task<Result<EquipmentDto>> UpdateAsync(
        Guid id,
        UpdateEquipmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var equipment = await repository.GetByIdAsync(id, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<EquipmentDto>(Error.Validation("Equipment.InvalidName", "设备名称不能为空"));
        }

        equipment.UpdateBasicInfo(
            request.Name,
            request.Model,
            request.SerialNumber,
            request.WorkCenterId,
            request.LineName,
            request.Remark);

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(equipment, true));
    }

    public async Task<Result<EquipmentDto>> ChangeStatusAsync(
        Guid id,
        ChangeEquipmentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var equipment = await repository.GetByIdAsync(id, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }
        if (request.Status == EquipmentStatus.Down && string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return Result.Failure<EquipmentDto>(Error.Validation("Equipment.DownReasonRequired", "故障停机必须填写停机原因"));
        }

        var log = equipment.ChangeStatus(request.Status, request.ReasonCode, request.Reason, currentUser.UserId);
        if (log is null)
        {
            return Result.Success(ToDto(equipment, true));
        }

        // 新子实体必须显式持久化
        repository.AddStatusLog(log);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(equipment, true));
    }

    public async Task<Result<EquipmentDto>> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var equipment = await repository.GetByIdAsync(id, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }

        equipment.SetActive(isActive);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(equipment, true));
    }

    public async Task<Result<EquipmentDto>> AddMaintenanceAsync(
        Guid id,
        CreateMaintenanceRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var equipment = await repository.GetByIdAsync(id, cancellationToken);
        if (equipment is null)
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Result.Failure<EquipmentDto>(Error.Validation("Equipment.InvalidContent", "点检 / 保养内容不能为空"));
        }

        var record = equipment.AddMaintenanceRecord(
            request.Type,
            request.Content,
            request.Result,
            request.AbnormalDescription,
            currentUser.UserId);

        repository.AddMaintenanceRecord(record);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(equipment, true));
    }

    public async Task<Result<EquipmentStatusSummaryDto>> GetStatusSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var counts = await repository.CountByStatusAsync(cancellationToken);
        var lookup = counts.ToDictionary(x => x.Status, x => x.Count);

        var summary = new EquipmentStatusSummaryDto(
            lookup.GetValueOrDefault(EquipmentStatus.Running),
            lookup.GetValueOrDefault(EquipmentStatus.Idle),
            lookup.GetValueOrDefault(EquipmentStatus.Down),
            lookup.GetValueOrDefault(EquipmentStatus.Maintenance),
            lookup.GetValueOrDefault(EquipmentStatus.Offline),
            lookup.Values.Sum());

        return Result.Success(summary);
    }

    public async Task<Result<IReadOnlyList<DowntimeParetoDto>>> GetDowntimeParetoAsync(
        DateTime? from = null,
        DateTime? to = null,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        var start = from ?? DateTime.UtcNow.AddDays(-30);
        var end = to ?? DateTime.UtcNow;
        var take = top is < 1 or > 50 ? 10 : top;

        var result = await repository.DowntimeParetoAsync(start, end, take, cancellationToken);
        return Result.Success<IReadOnlyList<DowntimeParetoDto>>(
            result.Select(x => new DowntimeParetoDto(x.ReasonCode, x.Count)).ToList());
    }

    private static Result<EquipmentDto> NotFound()
        => Result.Failure<EquipmentDto>(Error.NotFound("Equipment.NotFound", "设备不存在"));

    private static EquipmentDto ToDto(EquipmentEntity equipment, bool includeDetails) => new(
        equipment.Id,
        equipment.Code,
        equipment.Name,
        equipment.Model,
        equipment.SerialNumber,
        equipment.WorkCenterId,
        equipment.LineName,
        equipment.Status,
        equipment.StatusReason,
        equipment.DownReasonCode,
        equipment.StatusChangedAt,
        equipment.TotalDownSeconds,
        equipment.CurrentStatusSeconds,
        equipment.IsActive,
        equipment.Remark,
        equipment.CreatedAt,
        equipment.UpdatedAt,
        includeDetails
            ? equipment.StatusLogs
                .OrderByDescending(l => l.ChangedAt)
                .Take(50)
                .Select(l => new EquipmentStatusLogDto(
                    l.Id, l.FromStatus, l.ToStatus, l.ReasonCode, l.Reason, l.OperatorId, l.ChangedAt))
                .ToList()
            : [],
        includeDetails
            ? equipment.MaintenanceRecords
                .OrderByDescending(r => r.ExecutedAt)
                .Take(50)
                .Select(r => new MaintenanceRecordDto(
                    r.Id, r.Type, r.Content, r.Result, r.AbnormalDescription, r.ExecutorId, r.ExecutedAt))
                .ToList()
            : []);
}
