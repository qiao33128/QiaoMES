namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单号「按日计数」记录。
/// <para>
/// 每个自然日一行，记录当天已发放的最大序号；取号通过一条 UPSERT 语句原子完成。
/// 该实体不承载业务规则，仅用于保证单号生成的并发安全与可审计。
/// </para>
/// </summary>
public class WorkOrderDailySequence
{
    private WorkOrderDailySequence() { }

    public WorkOrderDailySequence(DateOnly sequenceDate)
    {
        SequenceDate = sequenceDate;
        LastValue = 0;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>序号日期（主键）。</summary>
    public DateOnly SequenceDate { get; private set; }

    /// <summary>当天已发放到的序号。</summary>
    public int LastValue { get; private set; }

    /// <summary>最后更新时间。</summary>
    public DateTime UpdatedAt { get; private set; }
}
