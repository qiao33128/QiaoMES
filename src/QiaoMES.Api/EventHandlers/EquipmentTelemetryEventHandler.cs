using QiaoMES.Equipment.Application;
using QiaoMES.Equipment.Application.Contracts;
using QiaoMES.Equipment.Domain;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Api.EventHandlers;

/// <summary>
/// 订阅「设备数据采集上报」事件：按设备编码定位设备并更新状态。<para>
/// 开放 API 只负责接收并落库（Outbox），真正的状态机变更由设备模块处理，
/// 这样采集侧（MQTT / OPC UA 网关）与设备模块解耦。
/// </para>
/// </summary>
public sealed class EquipmentTelemetryEventHandler(
    IEquipmentService equipmentService,
    ILogger<EquipmentTelemetryEventHandler> logger) : IIntegrationEventHandler<EquipmentTelemetryReceivedEvent>
{
    public async Task HandleAsync(EquipmentTelemetryReceivedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var lookup = await equipmentService.GetListAsync(
            new EquipmentQueryRequest(Keyword: integrationEvent.EquipmentCode, Page: 1, PageSize: 10),
            cancellationToken);

        if (lookup.IsFailure)
        {
            throw new InvalidOperationException($"设备查询失败：{lookup.Error.Code} {lookup.Error.Description}");
        }

        var equipment = lookup.Value.Items
            .FirstOrDefault(item => string.Equals(item.Code, integrationEvent.EquipmentCode, StringComparison.OrdinalIgnoreCase));

        if (equipment is null)
        {
            // 采集到未登记的设备：记录告警但不重试（重试也不会成功）
            logger.LogWarning("收到未登记设备的上报，已忽略：{EquipmentCode}", integrationEvent.EquipmentCode);
            return;
        }

        if ((int)equipment.Status == integrationEvent.Status)
        {
            return;
        }

        var result = await equipmentService.ChangeStatusAsync(
            equipment.Id,
            new ChangeEquipmentStatusRequest(
                (EquipmentStatus)integrationEvent.Status,
                integrationEvent.ReasonCode,
                integrationEvent.Reason),
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"设备状态更新失败（{integrationEvent.EquipmentCode}）：{result.Error.Code} {result.Error.Description}");
        }

        logger.LogInformation(
            "设备采集上报已应用：{EquipmentCode} → {Status}（原因 {Reason}）",
            integrationEvent.EquipmentCode,
            (EquipmentStatus)integrationEvent.Status,
            integrationEvent.ReasonCode ?? "-");
    }
}
