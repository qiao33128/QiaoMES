namespace QiaoMES.Shared;

/// <summary>
/// 提交后动作队列。
/// <para>
/// 数据库事务提交成功之后才执行的副作用（实时通知、集成事件、缓存失效等）都应通过本接口登记，
/// 避免出现「通知已推送、但数据被回滚」的不一致。
/// </para>
/// </summary>
public interface IPostCommitActions
{
    /// <summary>是否已有待执行动作。</summary>
    bool HasActions { get; }

    /// <summary>登记一个在事务提交成功后执行的动作。</summary>
    void Enqueue(Func<CancellationToken, Task> action);

    /// <summary>按登记顺序执行全部动作，并清空队列。单个动作失败不应影响后续动作。</summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
