using QiaoMES.Shared;

namespace QiaoMES.Production.Domain;

/// <summary>SN（序列号 / 单颗产品）的状态。</summary>
public enum SerialNumberStatus
{
    /// <summary>在制。</summary>
    InProcess = 0,

    /// <summary>已完工。</summary>
    Completed = 1,

    /// <summary>已报废。</summary>
    Scrapped = 2,

    /// <summary>已挂起（待处理）。</summary>
    OnHold = 3,
}

/// <summary>过站动作。</summary>
public enum WipAction
{
    /// <summary>进站。</summary>
    TrackIn = 0,

    /// <summary>出站。</summary>
    TrackOut = 1,
}

/// <summary>过站结果。</summary>
public enum WipResult
{
    /// <summary>进站时无结果。</summary>
    None = 0,

    /// <summary>合格。</summary>
    Pass = 1,

    /// <summary>不合格。</summary>
    Fail = 2,
}

/// <summary>
/// 序列号（SN）：一颗在制品。通过过站记录形成完整流转轨迹，是正向/反向追溯的基础。
/// </summary>
public class SerialNumber : Entity
{
    private SerialNumber() { }

    public SerialNumber(string sn, Guid workOrderId, Guid productId, string productCode)
        : base(Guid.NewGuid())
    {
        Sn = sn.Trim();
        WorkOrderId = workOrderId;
        ProductId = productId;
        ProductCode = productCode;
        Status = SerialNumberStatus.InProcess;
        CreatedAt = DateTime.UtcNow;
    }

    public string Sn { get; private set; } = string.Empty;

    public Guid WorkOrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductCode { get; private set; } = string.Empty;

    /// <summary>当前所在工序任务（未进站时为空）。</summary>
    public Guid? CurrentOperationTaskId { get; private set; }

    public string? CurrentOperationName { get; private set; }

    /// <summary>已完成的最后一道工序名称。</summary>
    public string? LastCompletedOperationName { get; private set; }

    public SerialNumberStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    private readonly List<WipTracking> _trackings = [];
    public IReadOnlyCollection<WipTracking> Trackings => _trackings.AsReadOnly();

    /// <summary>
    /// 进站：进入某道工序。
    /// </summary>
    public Result<WipTracking> TrackIn(
        Guid operationTaskId,
        string operationName,
        Guid? operatorId = null,
        Guid? equipmentId = null,
        string? remark = null)
    {
        var stateError = EnsureTrackable();
        if (stateError is not null)
        {
            return Result.Failure<WipTracking>(stateError);
        }

        if (CurrentOperationTaskId == operationTaskId)
        {
            return Result.Failure<WipTracking>(Error.Conflict("SerialNumber.AlreadyTrackedIn", "该 SN 已在本工序中"));
        }
        if (CurrentOperationTaskId is not null)
        {
            return Result.Failure<WipTracking>(Error.Conflict(
                "SerialNumber.NotTrackedOut",
                $"该 SN 仍停留在工序「{CurrentOperationName}」，请先出站"));
        }

        CurrentOperationTaskId = operationTaskId;
        CurrentOperationName = operationName;

        var tracking = new WipTracking(Id, WorkOrderId, operationTaskId, operationName, WipAction.TrackIn, WipResult.None, operatorId, equipmentId, remark);
        _trackings.Add(tracking);
        return Result.Success(tracking);
    }

    /// <summary>
    /// 出站：离开当前工序并给出结果。
    /// </summary>
    /// <param name="isLastOperation">是否为该工单的最后一道工序（合格则整颗完工）。</param>
    public Result<WipTracking> TrackOut(
        Guid operationTaskId,
        string operationName,
        WipResult result,
        bool isLastOperation,
        Guid? operatorId = null,
        Guid? equipmentId = null,
        string? remark = null)
    {
        var stateError = EnsureTrackable();
        if (stateError is not null)
        {
            return Result.Failure<WipTracking>(stateError);
        }
        if (result == WipResult.None)
        {
            return Result.Failure<WipTracking>(Error.Validation("SerialNumber.InvalidResult", "出站必须给出合格或不合格结果"));
        }
        if (CurrentOperationTaskId != operationTaskId)
        {
            return Result.Failure<WipTracking>(Error.Conflict(
                "SerialNumber.NotInOperation",
                $"该 SN 当前不在工序「{operationName}」中，无法出站"));
        }

        var tracking = new WipTracking(Id, WorkOrderId, operationTaskId, operationName, WipAction.TrackOut, result, operatorId, equipmentId, remark);
        _trackings.Add(tracking);

        CurrentOperationTaskId = null;

        if (result == WipResult.Pass)
        {
            CurrentOperationName = null;
            LastCompletedOperationName = operationName;

            if (isLastOperation)
            {
                Status = SerialNumberStatus.Completed;
                CompletedAt = DateTime.UtcNow;
            }
        }
        else
        {
            // 不合格：保留在当前工序，等待返修或报废处理
            CurrentOperationTaskId = operationTaskId;
            CurrentOperationName = operationName;
        }

        return Result.Success(tracking);
    }

    /// <summary>报废。</summary>
    public Result Scrap(string? remark = null)
    {
        if (Status == SerialNumberStatus.Completed)
        {
            return Result.Failure(Error.Conflict("SerialNumber.AlreadyCompleted", "已完工的 SN 不能报废"));
        }

        Status = SerialNumberStatus.Scrapped;
        Remark = remark?.Trim();
        CurrentOperationTaskId = null;
        return Result.Success();
    }

    public string? Remark { get; private set; }

    private Error? EnsureTrackable()
    {
        return Status switch
        {
            SerialNumberStatus.Completed => Error.Conflict("SerialNumber.AlreadyCompleted", "该 SN 已完工"),
            SerialNumberStatus.Scrapped => Error.Conflict("SerialNumber.Scrapped", "该 SN 已报废"),
            SerialNumberStatus.OnHold => Error.Conflict("SerialNumber.OnHold", "该 SN 已挂起，需先解挂"),
            _ => null,
        };
    }
}

/// <summary>
/// 过站记录（WIP 流转轨迹）。每一次进站 / 出站都留痕。
/// </summary>
public class WipTracking : Entity
{
    private WipTracking() { }

    public WipTracking(
        Guid serialNumberId,
        Guid workOrderId,
        Guid workOrderOperationId,
        string operationName,
        WipAction action,
        WipResult result,
        Guid? operatorId = null,
        Guid? equipmentId = null,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        SerialNumberId = serialNumberId;
        WorkOrderId = workOrderId;
        WorkOrderOperationId = workOrderOperationId;
        OperationName = operationName;
        Action = action;
        Result = result;
        OperatorId = operatorId;
        EquipmentId = equipmentId;
        Remark = remark?.Trim();
        TrackedAt = DateTime.UtcNow;
    }

    public Guid SerialNumberId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid WorkOrderOperationId { get; private set; }

    /// <summary>工序名称（过站当时的快照）。</summary>
    public string OperationName { get; private set; } = string.Empty;

    public WipAction Action { get; private set; }

    public WipResult Result { get; private set; }

    public Guid? OperatorId { get; private set; }

    public Guid? EquipmentId { get; private set; }

    public string? Remark { get; private set; }

    public DateTime TrackedAt { get; private set; }
}
