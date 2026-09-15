using QiaoMES.Shared;

namespace QiaoMES.Quality.Domain;

/// <summary>检验类型。</summary>
public enum InspectionType
{
    /// <summary>来料检验。</summary>
    Iqc = 0,

    /// <summary>过程检验（工序间）。</summary>
    Ipqc = 1,

    /// <summary>成品检验。</summary>
    Fqc = 2,

    /// <summary>出货检验。</summary>
    Oqc = 3,
}

/// <summary>检验单状态。</summary>
public enum InspectionStatus
{
    /// <summary>待检验。</summary>
    Pending = 0,

    /// <summary>检验中（已录入部分项）。</summary>
    InProgress = 1,

    /// <summary>已判定合格。</summary>
    Passed = 2,

    /// <summary>已判定不合格（需走处置流程）。</summary>
    Failed = 3,

    /// <summary>已让步接收（不合格但特许使用）。</summary>
    Concessioned = 4,
}

/// <summary>检验结论。</summary>
public enum InspectionConclusion
{
    None = 0,
    Pass = 1,
    Fail = 2,
    Concession = 3,
}

/// <summary>
/// 检验单。覆盖 IQC / IPQC / FQC / OQC 四类，支持抽样数、允收数（Ac）与拒收数（Re）。
/// </summary>
public class Inspection : Entity
{
    private Inspection() { }

    public Inspection(
        string inspectionNumber,
        InspectionType type,
        int sampleSize,
        Guid? workOrderId = null,
        Guid? workOrderOperationId = null,
        string? sn = null,
        Guid? materialId = null,
        string? materialCode = null,
        string? lotNumber = null,
        string? productCode = null,
        string? aqlLevel = null,
        int acceptedLimit = 0,
        int rejectedLimit = 1)
        : base(Guid.NewGuid())
    {
        InspectionNumber = inspectionNumber;
        Type = type;
        SampleSize = sampleSize;
        WorkOrderId = workOrderId;
        WorkOrderOperationId = workOrderOperationId;
        Sn = sn?.Trim();
        MaterialId = materialId;
        MaterialCode = materialCode;
        LotNumber = lotNumber?.Trim();
        ProductCode = productCode;
        AqlLevel = aqlLevel;
        AcceptedLimit = acceptedLimit;
        RejectedLimit = rejectedLimit;
        Status = InspectionStatus.Pending;
        Conclusion = InspectionConclusion.None;
        CreatedAt = DateTime.UtcNow;
    }

    public string InspectionNumber { get; private set; } = string.Empty;

    public InspectionType Type { get; private set; }

    public Guid? WorkOrderId { get; private set; }

    public Guid? WorkOrderOperationId { get; private set; }

    /// <summary>受检 SN（成品/过程检验时填写）。</summary>
    public string? Sn { get; private set; }

    /// <summary>受检物料（IQC 时填写）。</summary>
    public Guid? MaterialId { get; private set; }

    public string? MaterialCode { get; private set; }

    /// <summary>关联的来料批次号（IQC 填写，用于上游谱系追溯）。</summary>
    public string? LotNumber { get; private set; }

    public string? ProductCode { get; private set; }

    /// <summary>抽样数。</summary>
    public int SampleSize { get; private set; }

    /// <summary>抽样标准，如 <c>AQL 1.0</c>。</summary>
    public string? AqlLevel { get; private set; }

    /// <summary>允收数 Ac。</summary>
    public int AcceptedLimit { get; private set; }

    /// <summary>拒收数 Re。</summary>
    public int RejectedLimit { get; private set; }

    public InspectionStatus Status { get; private set; }

    public InspectionConclusion Conclusion { get; private set; }

    /// <summary>不良数（不合格的检验项对应的样品数）。</summary>
    public int DefectQuantity { get; private set; }

    public Guid? InspectorId { get; private set; }

    public string? InspectorName { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? InspectedAt { get; private set; }

    public string? Remark { get; private set; }

    private readonly List<InspectionItem> _items = [];
    public IReadOnlyCollection<InspectionItem> Items => _items.AsReadOnly();

    public IReadOnlyList<InspectionItem> OrderedItems => _items.OrderBy(i => i.Sequence).ToList();

    /// <summary>不合格项数量。</summary>
    public int FailedItemCount => _items.Count(i => i.IsQualified == false);

    /// <summary>是否所有项都已判定。</summary>
    public bool IsFullyRecorded => _items.Count > 0 && _items.All(i => i.IsQualified is not null);

    /// <summary>
    /// 添加检验项。
    /// </summary>
    /// <returns>新建的检验项；聚合已被跟踪时调用方需显式持久化。</returns>
    public InspectionItem AddItem(
        string name,
        string? standard = null,
        decimal? lowerLimit = null,
        decimal? upperLimit = null,
        bool isKeyItem = false,
        string? remark = null)
    {
        var sequence = _items.Count == 0 ? 10 : _items.Max(i => i.Sequence) + 10;
        var item = new InspectionItem(Id, sequence, name, standard, lowerLimit, upperLimit, isKeyItem, remark);
        _items.Add(item);
        return item;
    }

    /// <summary>录入某个检验项的实测结果（定量项按规格上下限自动判定）。</summary>
    public Result RecordItem(
        Guid itemId,
        string? measuredValue,
        decimal? numericValue,
        bool? isQualified = null,
        string? defectCode = null,
        string? remark = null)
    {
        if (IsFinished)
        {
            return Result.Failure(Error.Conflict("Inspection.AlreadyJudged", "检验单已判定，不能再录入结果"));
        }

        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Inspection.ItemNotFound", "检验项不存在"));
        }

        item.Record(measuredValue, numericValue, isQualified, defectCode, remark);

        if (Status == InspectionStatus.Pending)
        {
            Status = InspectionStatus.InProgress;
        }

        return Result.Success();
    }

    /// <summary>
    /// 提交判定。自动按检验项结果推导结论（也可显式让步接收）。
    /// </summary>
    public Result Submit(
        int defectQuantity,
        Guid? inspectorId = null,
        string? inspectorName = null,
        bool concession = false,
        string? remark = null)
    {
        if (IsFinished)
        {
            return Result.Failure(Error.Conflict("Inspection.AlreadyJudged", "检验单已判定"));
        }
        if (_items.Count == 0)
        {
            return Result.Failure(Error.Validation("Inspection.NoItems", "检验单没有检验项"));
        }
        if (_items.Any(i => i.IsQualified is null))
        {
            return Result.Failure(Error.Validation(
                "Inspection.ItemNotRecorded",
                $"还有 {_items.Count(i => i.IsQualified is null)} 个检验项未录入结果"));
        }
        if (defectQuantity < 0 || defectQuantity > SampleSize)
        {
            return Result.Failure(Error.Validation("Inspection.InvalidDefectQuantity", "不良数必须在 0 到抽样数之间"));
        }

        var hasFailedItem = _items.Any(i => i.IsQualified == false);

        // 有不合格项（或不良数达到拒收数）→ 不合格
        if (hasFailedItem || defectQuantity >= RejectedLimit)
        {
            Conclusion = concession ? InspectionConclusion.Concession : InspectionConclusion.Fail;
            Status = concession ? InspectionStatus.Concessioned : InspectionStatus.Failed;
        }
        else
        {
            Conclusion = InspectionConclusion.Pass;
            Status = InspectionStatus.Passed;
        }

        DefectQuantity = defectQuantity;
        InspectorId = inspectorId;
        InspectorName = inspectorName;
        Remark = remark?.Trim();
        InspectedAt = DateTime.UtcNow;

        return Result.Success();
    }

    /// <summary>是否已出结论。</summary>
    public bool IsFinished => Status is InspectionStatus.Passed
        or InspectionStatus.Failed
        or InspectionStatus.Concessioned;

    /// <summary>是否需要走不合格处置流程。</summary>
    public bool NeedsDisposition => Status == InspectionStatus.Failed;
}

/// <summary>
/// 检验项（检验单内的检查条目）。支持定性判定与定量（带规格上下限）自动判定。
/// </summary>
public class InspectionItem : Entity
{
    private InspectionItem() { }

    public InspectionItem(
        Guid inspectionId,
        int sequence,
        string name,
        string? standard = null,
        decimal? lowerLimit = null,
        decimal? upperLimit = null,
        bool isKeyItem = false,
        string? remark = null)
        : base(Guid.NewGuid())
    {
        InspectionId = inspectionId;
        Sequence = sequence;
        Name = name.Trim();
        Standard = standard?.Trim();
        LowerLimit = lowerLimit;
        UpperLimit = upperLimit;
        IsKeyItem = isKeyItem;
        Remark = remark?.Trim();
    }

    public Guid InspectionId { get; private set; }

    public int Sequence { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>标准 / 要求描述。</summary>
    public string? Standard { get; private set; }

    /// <summary>规格下限（定量项）。</summary>
    public decimal? LowerLimit { get; private set; }

    /// <summary>规格上限（定量项）。</summary>
    public decimal? UpperLimit { get; private set; }

    /// <summary>是否关键项（关键项不合格直接判退）。</summary>
    public bool IsKeyItem { get; private set; }

    /// <summary>实测值（原始文本，便于记录非数值结果）。</summary>
    public string? MeasuredValue { get; private set; }

    /// <summary>实测数值（定量项）。</summary>
    public decimal? NumericValue { get; private set; }

    public bool? IsQualified { get; private set; }

    /// <summary>不良代码。</summary>
    public string? DefectCode { get; private set; }

    public string? Remark { get; private set; }

    /// <summary>是否定量项（有规格上下限）。</summary>
    public bool IsQuantitative => LowerLimit is not null || UpperLimit is not null;

    /// <summary>录入结果。未显式给出判定时，定量项按规格上下限自动判定。</summary>
    public void Record(
        string? measuredValue,
        decimal? numericValue,
        bool? isQualified,
        string? defectCode,
        string? remark)
    {
        MeasuredValue = measuredValue?.Trim();
        NumericValue = numericValue ?? TryParse(measuredValue);
        IsQualified = isQualified ?? Judge(NumericValue);
        DefectCode = defectCode?.Trim();
        Remark = remark?.Trim();
    }

    private bool? Judge(decimal? value)
    {
        if (!IsQuantitative || value is null)
        {
            return null;
        }

        if (LowerLimit is not null && value < LowerLimit)
        {
            return false;
        }
        if (UpperLimit is not null && value > UpperLimit)
        {
            return false;
        }

        return true;
    }

    private static decimal? TryParse(string? value)
        => decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
