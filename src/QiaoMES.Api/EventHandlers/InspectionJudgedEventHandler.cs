using QiaoMES.Equipment.Application;
using QiaoMES.Equipment.Application.Contracts;
using QiaoMES.Equipment.Domain;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Api.EventHandlers;

/// <summary>
/// 订阅「检验单已判定」事件：检验不合格时自动在**设备模块**发起 Andon 呼叫（质量异常红灯）。<para>
/// 这正是事件化的价值 —— 质量模块完全不知道设备模块的存在，两者的装配只在组合根完成。
/// </para>
/// <para>
/// 幂等：Outbox 至少投递一次，同一检验单若已发起过呼叫则直接跳过（按呼叫描述里的检验单号判重）。
/// </para>
/// </summary>
public sealed class InspectionJudgedEventHandler(
    IAndonService andonService,
    ILogger<InspectionJudgedEventHandler> logger) : IIntegrationEventHandler<InspectionJudgedEvent>
{
    /// <summary>检验单状态：不合格（InspectionStatus.Failed）。</summary>
    private const int FailedStatus = 3;

    public async Task HandleAsync(InspectionJudgedEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent.Status != FailedStatus)
        {
            return;
        }

        // 幂等判重
        var openCalls = await andonService.GetListAsync(
            new AndonQueryRequest(OnlyOpen: true, Page: 1, PageSize: 100), cancellationToken);

        if (openCalls.IsSuccess
            && openCalls.Value.Items.Any(call => call.Description.Contains(
                integrationEvent.InspectionNumber, StringComparison.Ordinal)))
        {
            logger.LogDebug(
                "检验单 {InspectionNumber} 已发起过 Andon 呼叫，跳过（幂等）",
                integrationEvent.InspectionNumber);
            return;
        }

        var result = await andonService.CreateAsync(
            new CreateAndonCallRequest(
                AndonType.QualityIssue,
                BuildDescription(integrationEvent),
                AndonLevel.Red,
                Sn: integrationEvent.Sn,
                TimeoutMinutes: 10),
            cancellationToken);

        if (result.IsFailure)
        {
            // 抛出 → Outbox 记录失败并按指数退避重试
            throw new InvalidOperationException(
                $"检验不合格自动发起 Andon 呼叫失败：{result.Error.Code} {result.Error.Description}");
        }

        logger.LogInformation(
            "检验不合格 → 自动发起 Andon 呼叫 {CallNumber}（检验单 {InspectionNumber}，SN={Sn}）",
            result.Value.CallNumber,
            integrationEvent.InspectionNumber,
            integrationEvent.Sn ?? "-");
    }

    private static string BuildDescription(InspectionJudgedEvent integrationEvent)
    {
        var parts = new List<string> { $"检验不合格：{integrationEvent.InspectionNumber}" };

        if (!string.IsNullOrWhiteSpace(integrationEvent.ProductCode))
        {
            parts.Add($"产品 {integrationEvent.ProductCode}");
        }
        if (!string.IsNullOrWhiteSpace(integrationEvent.DefectCode))
        {
            parts.Add($"不良代码 {integrationEvent.DefectCode}");
        }
        if (integrationEvent.DefectQuantity > 0)
        {
            parts.Add($"不良数 {integrationEvent.DefectQuantity}");
        }

        return string.Join("；", parts);
    }
}
