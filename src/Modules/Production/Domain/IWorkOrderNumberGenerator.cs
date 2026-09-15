namespace QiaoMES.Production.Domain;

/// <summary>
/// 工单号生成器。
/// <para>实现必须保证并发安全（多个请求同时创建工单时不得产生重复单号）。</para>
/// </summary>
public interface IWorkOrderNumberGenerator
{
    /// <summary>生成下一个工单号，格式 <c>WO-yyyyMMdd-NNNN</c>（按日期独立递增）。</summary>
    Task<string> NextAsync(DateTime now, CancellationToken cancellationToken = default);
}
