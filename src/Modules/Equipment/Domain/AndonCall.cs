using QiaoMES.Shared;

namespace QiaoMES.Equipment.Domain;

/// <summary>Andon 呼叫类型。</summary>
public enum AndonType
{
    /// <summary>设备故障。</summary>
    EquipmentFailure = 0,

    /// <summary>质量异常。</summary>
    QualityIssue = 1,

    /// <summary>缺料 / 待料。</summary>
    MaterialShortage = 2,

    /// <summary>其它异常。</summary>
    Other = 3,
}

/// <summary>Andon 级别（看板红黄灯）。</summary>
public enum AndonLevel
{
    /// <summary>黄灯（一般，需关注）。</summary>
    Yellow = 0,

    /// <summary>红灯（严重，需立即处理）。</summary>
    Red = 1,
}

/// <summary>Andon 状态。</summary>
public enum AndonStatus
{
    /// <summary>待响应。</summary>
    Waiting = 0,

    /// <summary>已响应（处理中）。</summary>
    Responded = 1,

    /// <summary>已解决。</summary>
    Resolved = 2,

    /// <summary>已关闭。</summary>
    Closed = 3,
}

/// <summary>
/// Andon 呼叫单：现场一键呼叫 → 响应 → 解决 → 关闭；超时未响应自动升级。
/// </summary>
public class AndonCall : Entity
{
    private AndonCall() { }

    public AndonCall(
        string callNumber,
        AndonType type,
        string description,
        AndonLevel level = AndonLevel.Yellow,
        Guid? equipmentId = null,
        string? equipmentCode = null,
        Guid? workCenterId = null,
        string? workCenterName = null,
        string? sn = null,
        int timeoutMinutes = 10,
        Guid? callerId = null)
        : base(Guid.NewGuid())
    {
        CallNumber = callNumber;
        Type = type;
        Description = description.Trim();
        Level = level;
        EquipmentId = equipmentId;
        EquipmentCode = equipmentCode;
        WorkCenterId = workCenterId;
        WorkCenterName = workCenterName;
        Sn = sn?.Trim();
        TimeoutMinutes = timeoutMinutes <= 0 ? 10 : timeoutMinutes;
        CallerId = callerId;
        Status = AndonStatus.Waiting;
        CalledAt = DateTime.UtcNow;
    }

    public string CallNumber { get; private set; } = string.Empty;

    public AndonType Type { get; private set; }

    public AndonLevel Level { get; private set; }

    public AndonStatus Status { get; private set; }

    public Guid? EquipmentId { get; private set; }

    public string? EquipmentCode { get; private set; }

    public Guid? WorkCenterId { get; private set; }

    public string? WorkCenterName { get; private set; }

    /// <summary>质量异常关联的 SN。</summary>
    public string? Sn { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public Guid? CallerId { get; private set; }

    public DateTime CalledAt { get; private set; }

    /// <summary>响应时限（分钟）。</summary>
    public int TimeoutMinutes { get; private set; }

    public DateTime? RespondedAt { get; private set; }

    public Guid? ResponderId { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public string? Resolution { get; private set; }

    /// <summary>是否已超时升级。</summary>
    public bool Escalated { get; private set; }

    public DateTime? EscalatedAt { get; private set; }

    /// <summary>是否已结束。</summary>
    public bool IsFinished => Status is AndonStatus.Resolved or AndonStatus.Closed;

    /// <summary>是否超时未响应（用于看板闪烁告警）。</summary>
    public bool IsTimeout(DateTime now)
        => Status == AndonStatus.Waiting && now > CalledAt.AddMinutes(TimeoutMinutes);

    /// <summary>响应呼叫。</summary>
    public Result Respond(Guid? responderId = null)
    {
        if (Status != AndonStatus.Waiting)
        {
            return Result.Failure(Error.Conflict("Andon.AlreadyResponded", "该呼叫已被响应"));
        }

        Status = AndonStatus.Responded;
        RespondedAt = DateTime.UtcNow;
        ResponderId = responderId;
        return Result.Success();
    }

    /// <summary>解决呼叫。</summary>
    public Result Resolve(string? resolution = null)
    {
        if (IsFinished)
        {
            return Result.Failure(Error.Conflict("Andon.AlreadyFinished", "该呼叫已结束"));
        }

        Status = AndonStatus.Resolved;
        ResolvedAt = DateTime.UtcNow;
        Resolution = resolution?.Trim();
        return Result.Success();
    }

    /// <summary>关闭呼叫。</summary>
    public Result Close(string? resolution = null)
    {
        if (Status == AndonStatus.Closed)
        {
            return Result.Failure(Error.Conflict("Andon.AlreadyClosed", "该呼叫已关闭"));
        }

        Resolution = resolution?.Trim() ?? Resolution;
        Status = AndonStatus.Closed;
        ResolvedAt ??= DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>标记超时升级（由后台任务调用）。</summary>
    public bool Escalate(DateTime now)
    {
        if (Escalated || Status != AndonStatus.Waiting)
        {
            return false;
        }

        Escalated = true;
        EscalatedAt = now;
        // 超时未响应自动升为红灯
        Level = AndonLevel.Red;
        return true;
    }
}
