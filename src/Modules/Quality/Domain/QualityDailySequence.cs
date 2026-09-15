namespace QiaoMES.Quality.Domain;

/// <summary>
/// 质量单号日序列（检验单 / 处置单）。
/// <para>由数据库原子分配（INSERT ... ON CONFLICT DO UPDATE ... RETURNING），并发下不会重号。</para>
/// </summary>
public class QualityDailySequence
{
    private QualityDailySequence() { }

    /// <summary>序列键，形如 <c>IQC-20260915</c>、<c>NC-20260915</c>。</summary>
    public string SequenceKey { get; private set; } = string.Empty;

    public int LastValue { get; private set; }

    public DateTime UpdatedAt { get; private set; }
}
