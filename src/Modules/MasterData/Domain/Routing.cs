using QiaoMES.Shared;

namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 工艺路线。按「产品 + 版本」管理，同一产品同时只有一个生效版本。
/// <para>工单下达时会按生效版本展开工序任务，因此生效版本应尽量保持稳定。</para>
/// </summary>
public class Routing : Entity
{
    private Routing() { }

    public Routing(Guid productId, string version, string? remark = null)
        : base(Guid.NewGuid())
    {
        ProductId = productId;
        Version = version.Trim();
        Remark = remark?.Trim();
        IsActive = false;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid ProductId { get; private set; }

    public string Version { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private readonly List<RoutingStep> _steps = [];
    public IReadOnlyCollection<RoutingStep> Steps => _steps.AsReadOnly();

    /// <summary>有效步骤（按顺序）。</summary>
    public IReadOnlyList<RoutingStep> OrderedSteps => _steps
        .Where(s => !s.IsDeleted)
        .OrderBy(s => s.Sequence)
        .ToList();

    /// <summary>整条路线的标准总工时（秒）。</summary>
    public int TotalStandardSeconds => OrderedSteps.Sum(s => s.StandardSeconds);

    /// <summary>
    /// 用给定集合整体替换工序步骤。
    /// </summary>
    /// <returns>新增的步骤列表，调用方需显式持久化。</returns>
    public IReadOnlyList<RoutingStep> ReplaceSteps(IEnumerable<RoutingStepSpec> specs)
    {
        _steps.Clear();

        var ordered = specs.OrderBy(s => s.Sequence).ToList();
        var added = new List<RoutingStep>();

        for (var index = 0; index < ordered.Count; index++)
        {
            var spec = ordered[index];
            var step = new RoutingStep(
                Id,
                // 顺序号归一化为 10/20/30…，便于后续插单
                (index + 1) * 10,
                spec.OperationId,
                spec.WorkCenterId,
                spec.StandardSeconds,
                spec.IsQualityGate);

            _steps.Add(step);
            added.Add(step);
        }

        Touch();
        return added;
    }

    public Result Activate()
    {
        if (OrderedSteps.Count == 0)
        {
            return Result.Failure(Error.Conflict("Routing.Empty", "工艺路线没有工序，不能设为生效版本"));
        }

        IsActive = true;
        Touch();
        return Result.Success();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Touch();
    }

    public void UpdateRemark(string? remark)
    {
        Remark = remark?.Trim();
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

/// <summary>工艺路线步骤的输入规格。</summary>
public sealed record RoutingStepSpec(
    int Sequence,
    Guid OperationId,
    Guid? WorkCenterId,
    int StandardSeconds,
    bool IsQualityGate);

/// <summary>
/// 工艺路线中的一个工序步骤。
/// </summary>
public class RoutingStep : Entity
{
    private RoutingStep() { }

    public RoutingStep(
        Guid routingId,
        int sequence,
        Guid operationId,
        Guid? workCenterId,
        int standardSeconds,
        bool isQualityGate)
        : base(Guid.NewGuid())
    {
        RoutingId = routingId;
        Sequence = sequence;
        OperationId = operationId;
        WorkCenterId = workCenterId;
        StandardSeconds = standardSeconds;
        IsQualityGate = isQualityGate;
    }

    public Guid RoutingId { get; private set; }

    /// <summary>工序顺序号（10、20、30…）。</summary>
    public int Sequence { get; private set; }

    public Guid OperationId { get; private set; }

    /// <summary>该工序的执行工作中心（未指定时取工序的默认工作中心）。</summary>
    public Guid? WorkCenterId { get; private set; }

    /// <summary>该工序的标准工时（秒）。</summary>
    public int StandardSeconds { get; private set; }

    /// <summary>是否为质检点（需检验合格才能流转到下一工序）。</summary>
    public bool IsQualityGate { get; private set; }
}
