using QiaoMES.Shared;

namespace QiaoMES.Equipment.Domain;

/// <summary>设备状态。</summary>
public enum EquipmentStatus
{
    /// <summary>运行中。</summary>
    Running = 0,

    /// <summary>待机。</summary>
    Idle = 1,

    /// <summary>故障停机。</summary>
    Down = 2,

    /// <summary>保养中。</summary>
    Maintenance = 3,

    /// <summary>离线 / 停用。</summary>
    Offline = 4,
}

/// <summary>
/// 设备台账（含当前状态与累计停机时长）。
/// </summary>
public class Equipment : Entity
{
    private Equipment() { }

    public Equipment(
        string code,
        string name,
        string? model = null,
        string? serialNumber = null,
        Guid? workCenterId = null,
        string? lineName = null,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        Code = code.Trim();
        Name = name.Trim();
        Model = model?.Trim();
        SerialNumber = serialNumber?.Trim();
        WorkCenterId = workCenterId;
        LineName = lineName?.Trim();
        Remark = remark?.Trim();
        Status = EquipmentStatus.Idle;
        StatusChangedAt = DateTime.UtcNow;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>型号。</summary>
    public string? Model { get; private set; }

    /// <summary>出厂序列号 / 资产编号。</summary>
    public string? SerialNumber { get; private set; }

    /// <summary>所属工作中心。</summary>
    public Guid? WorkCenterId { get; private set; }

    /// <summary>所属产线（文本，便于直接显示）。</summary>
    public string? LineName { get; private set; }

    public EquipmentStatus Status { get; private set; }

    /// <summary>当前状态原因（故障停机原因、保养项目等）。</summary>
    public string? StatusReason { get; private set; }

    /// <summary>停机原因代码。</summary>
    public string? DownReasonCode { get; private set; }

    /// <summary>进入当前状态的时间。</summary>
    public DateTime? StatusChangedAt { get; private set; }

    /// <summary>累计故障停机时长（秒）。</summary>
    public long TotalDownSeconds { get; private set; }

    public bool IsActive { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private readonly List<EquipmentStatusLog> _statusLogs = [];
    public IReadOnlyCollection<EquipmentStatusLog> StatusLogs => _statusLogs.AsReadOnly();

    private readonly List<EquipmentMaintenanceRecord> _maintenanceRecords = [];
    public IReadOnlyCollection<EquipmentMaintenanceRecord> MaintenanceRecords => _maintenanceRecords.AsReadOnly();

    /// <summary>当前状态已持续时长（秒）。</summary>
    public long CurrentStatusSeconds => StatusChangedAt is null
        ? 0
        : (long)(DateTime.UtcNow - StatusChangedAt.Value).TotalSeconds;

    /// <summary>是否处于故障停机。</summary>
    public bool IsDown => Status == EquipmentStatus.Down;

    /// <summary>
    /// 切换设备状态。
    /// </summary>
    /// <returns>状态变更记录；状态未变化时返回 <c>null</c>（调用方需显式持久化非 null 的返回值）。</returns>
    public EquipmentStatusLog? ChangeStatus(
        EquipmentStatus status,
        string? reasonCode = null,
        string? reason = null,
        Guid? operatorId = null)
    {
        if (Status == status)
        {
            return null;
        }

        var now = DateTime.UtcNow;

        // 离开故障状态时累计停机时长
        if (Status == EquipmentStatus.Down && StatusChangedAt is not null)
        {
            TotalDownSeconds += (long)(now - StatusChangedAt.Value).TotalSeconds;
        }

        var log = new EquipmentStatusLog(Id, Status, status, reasonCode, reason, operatorId, now);
        _statusLogs.Add(log);

        Status = status;
        StatusReason = reason?.Trim();
        DownReasonCode = status == EquipmentStatus.Down ? reasonCode?.Trim() : null;
        StatusChangedAt = now;
        UpdatedAt = now;

        return log;
    }

    /// <summary>登记点检 / 保养 / 维修记录。</summary>
    /// <returns>新建的记录；调用方需显式持久化。</returns>
    public EquipmentMaintenanceRecord AddMaintenanceRecord(
        EquipmentMaintenanceType type,
        string content,
        EquipmentMaintenanceResult result = EquipmentMaintenanceResult.Normal,
        string? abnormalDescription = null,
        Guid? executorId = null,
        DateTime? executedAt = null)
    {
        var record = new EquipmentMaintenanceRecord(
            Id,
            type,
            content,
            result,
            abnormalDescription,
            executorId,
            executedAt ?? DateTime.UtcNow);

        _maintenanceRecords.Add(record);
        UpdatedAt = DateTime.UtcNow;
        return record;
    }

    public void UpdateBasicInfo(string name, string? model, string? serialNumber, Guid? workCenterId, string? lineName, string? remark)
    {
        Name = name.Trim();
        Model = model?.Trim();
        SerialNumber = serialNumber?.Trim();
        WorkCenterId = workCenterId;
        LineName = lineName?.Trim();
        Remark = remark?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 设备状态变更记录（用于停机分析与状态轨迹）。
/// </summary>
public class EquipmentStatusLog : Entity
{
    private EquipmentStatusLog() { }

    public EquipmentStatusLog(
        Guid equipmentId,
        EquipmentStatus fromStatus,
        EquipmentStatus toStatus,
        string? reasonCode = null,
        string? reason = null,
        Guid? operatorId = null,
        DateTime? changedAt = null)
        : base(Guid.NewGuid())
    {
        EquipmentId = equipmentId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ReasonCode = reasonCode?.Trim();
        Reason = reason?.Trim();
        OperatorId = operatorId;
        ChangedAt = changedAt ?? DateTime.UtcNow;
    }

    public Guid EquipmentId { get; private set; }

    public EquipmentStatus FromStatus { get; private set; }

    public EquipmentStatus ToStatus { get; private set; }

    /// <summary>停机 / 变更原因代码。</summary>
    public string? ReasonCode { get; private set; }

    public string? Reason { get; private set; }

    public Guid? OperatorId { get; private set; }

    public DateTime ChangedAt { get; private set; }
}

/// <summary>设备保养 / 点检类型。</summary>
public enum EquipmentMaintenanceType
{
    /// <summary>日常点检。</summary>
    DailyCheck = 0,

    /// <summary>定期保养。</summary>
    Maintenance = 1,

    /// <summary>维修。</summary>
    Repair = 2,
}

/// <summary>保养 / 点检结果。</summary>
public enum EquipmentMaintenanceResult
{
    /// <summary>正常。</summary>
    Normal = 0,

    /// <summary>异常（需跟进）。</summary>
    Abnormal = 1,
}

/// <summary>
/// 设备点检 / 保养 / 维修记录。
/// </summary>
public class EquipmentMaintenanceRecord : Entity
{
    private EquipmentMaintenanceRecord() { }

    public EquipmentMaintenanceRecord(
        Guid equipmentId,
        EquipmentMaintenanceType type,
        string content,
        EquipmentMaintenanceResult result = EquipmentMaintenanceResult.Normal,
        string? abnormalDescription = null,
        Guid? executorId = null,
        DateTime? executedAt = null)
        : base(Guid.NewGuid())
    {
        EquipmentId = equipmentId;
        Type = type;
        Content = content.Trim();
        Result = result;
        AbnormalDescription = abnormalDescription?.Trim();
        ExecutorId = executorId;
        ExecutedAt = executedAt ?? DateTime.UtcNow;
    }

    public Guid EquipmentId { get; private set; }

    public EquipmentMaintenanceType Type { get; private set; }

    /// <summary>点检 / 保养内容。</summary>
    public string Content { get; private set; } = string.Empty;

    public EquipmentMaintenanceResult Result { get; private set; }

    /// <summary>异常描述（结果异常时填写）。</summary>
    public string? AbnormalDescription { get; private set; }

    public Guid? ExecutorId { get; private set; }

    public DateTime ExecutedAt { get; private set; }
}
