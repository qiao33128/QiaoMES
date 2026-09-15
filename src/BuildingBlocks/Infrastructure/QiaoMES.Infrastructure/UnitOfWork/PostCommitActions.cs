using Microsoft.Extensions.Logging;
using QiaoMES.Shared;

namespace QiaoMES.Infrastructure.UnitOfWork;

/// <summary>
/// <see cref="IPostCommitActions"/> 的请求级（Scoped）实现。
/// </summary>
public sealed class PostCommitActions(ILogger<PostCommitActions> logger) : IPostCommitActions
{
    private readonly List<Func<CancellationToken, Task>> _actions = [];

    public bool HasActions => _actions.Count > 0;

    public void Enqueue(Func<CancellationToken, Task> action) => _actions.Add(action);

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (_actions.Count == 0)
        {
            return;
        }

        var pending = _actions.ToArray();
        _actions.Clear();

        foreach (var action in pending)
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception exception)
            {
                // 提交已成功，副作用失败不能反过来影响已落库的数据，只记录告警
                logger.LogError(exception, "提交后动作执行失败，已忽略");
            }
        }
    }
}
