namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 工序（工艺路线的最小单元）。例如：SMT 印刷、贴片、回流焊、AOI 检验、组装、测试。
/// </summary>
public class Operation : CatalogEntity
{
    private Operation() { }

    public Operation(
        string code,
        string name,
        int standardSeconds = 0,
        bool isKeyOperation = false,
        Guid? defaultWorkCenterId = null,
        string? remark = null)
        : base(code, name, null, null, remark)
    {
        StandardSeconds = standardSeconds;
        IsKeyOperation = isKeyOperation;
        DefaultWorkCenterId = defaultWorkCenterId;
    }

    /// <summary>标准工时（秒 / 件）。</summary>
    public int StandardSeconds { get; private set; }

    /// <summary>是否关键工序（通常需要质检或重点管控）。</summary>
    public bool IsKeyOperation { get; private set; }

    /// <summary>默认工作中心。</summary>
    public Guid? DefaultWorkCenterId { get; private set; }

    public void UpdateProcessInfo(int standardSeconds, bool isKeyOperation, Guid? defaultWorkCenterId)
    {
        StandardSeconds = standardSeconds;
        IsKeyOperation = isKeyOperation;
        DefaultWorkCenterId = defaultWorkCenterId;
        UpdatedAt = DateTime.UtcNow;
    }
}
