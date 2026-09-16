namespace QiaoMES.Shared.IntegrationEvents;

/// <summary>
/// 集成事件（模块间通信契约）。<para>
/// 与领域事件的区别：领域事件是聚合内部的语言，集成事件是**跨模块对外发布的稳定契约**，
/// 一旦发布就不应随意改字段语义（需要变更时新增版本化事件），因为订阅方可能在不同进程甚至不同服务。
/// </para>
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>事件唯一编号（消费方据此幂等）。</summary>
    Guid EventId { get; }

    /// <summary>事件发生时间（UTC）。</summary>
    DateTime OccurredAt { get; }
}

/// <summary>集成事件基类：自动生成事件号与发生时间。</summary>
public abstract record IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>工单已下达（可以开工 / 生成 SN）。</summary>
public sealed record WorkOrderReleasedEvent(
    Guid WorkOrderId,
    string OrderNumber,
    string ProductCode,
    int PlannedQuantity,
    Guid? ReleasedBy) : IntegrationEvent;

/// <summary>
/// 检验单已判定。<para>
/// 订阅方（如设备模块、看板）据此做后续动作，无需反向依赖质量模块。
/// </para>
/// </summary>
/// <param name="Status">检验单状态（2 合格 / 3 不合格 / 4 让步接收）。</param>
/// <param name="Type">检验类型（0 IQC / 1 IPQC / 2 FQC / 3 OQC）。</param>
/// <param name="DefectCode">首个不合格项的缺陷代码（用于自动派工 / 报警）。</param>
public sealed record InspectionJudgedEvent(
    Guid InspectionId,
    string InspectionNumber,
    int Type,
    int Status,
    string? Sn,
    string? ProductCode,
    string? LotNumber,
    Guid? WorkOrderId,
    string? DefectCode,
    int DefectQuantity,
    string Conclusion) : IntegrationEvent;

/// <summary>SN 已完工（最后一道工序出站）。</summary>
public sealed record SerialNumberCompletedEvent(
    string Sn,
    Guid WorkOrderId,
    string ProductCode,
    Guid? CompletedBy) : IntegrationEvent;

/// <summary>Andon 呼叫超时未响应，已升级红灯。</summary>
public sealed record AndonCallEscalatedEvent(
    Guid CallId,
    string CallNumber,
    string? EquipmentCode,
    string? Description) : IntegrationEvent;

/// <summary>SN 已绑定来料批次（上游谱系建立）。</summary>
public sealed record MaterialConsumptionBoundEvent(
    string Sn,
    string LotNumber,
    string MaterialCode,
    decimal Quantity) : IntegrationEvent;

/// <summary>来料批次判定不合格（需要冻结 / 追溯影响范围）。</summary>
public sealed record MaterialLotRejectedEvent(
    string LotNumber,
    string MaterialCode,
    string? InspectionNumber,
    string? Reason) : IntegrationEvent;

/// <summary>
/// 设备数据采集上报（来自 MQTT / OPC UA 网关，经开放 API 接入）。
/// </summary>
/// <param name="Status">设备状态（0 运行 / 1 待机 / 2 故障 / 3 保养 / 4 离线）。</param>
public sealed record EquipmentTelemetryReceivedEvent(
    string EquipmentCode,
    int Status,
    string? ReasonCode,
    string? Reason,
    DateTime ReportedAt) : IntegrationEvent;
