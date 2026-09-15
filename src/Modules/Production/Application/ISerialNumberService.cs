using QiaoMES.Production.Application.Contracts;
using QiaoMES.Shared;

namespace QiaoMES.Production.Application;

/// <summary>
/// SN（序列号）与过站服务：WIP 流转与追溯查询。
/// </summary>
public interface ISerialNumberService
{
    /// <summary>为工单批量生成 SN（编号规则：<c>{工单号}-{序号:D4}</c>）。</summary>
    Task<Result<IReadOnlyList<SerialNumberDto>>> GenerateAsync(
        GenerateSerialNumbersRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<PagedResult<SerialNumberDto>>> GetListAsync(
        SerialNumberQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>按 SN 查询当前状态与完整过站轨迹。</summary>
    Task<Result<SerialNumberDetailDto>> GetBySnAsync(string sn, CancellationToken cancellationToken = default);

    Task<Result<SerialNumberDetailDto>> TrackInAsync(
        string sn,
        SnTrackInRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>出站：合格且为最后一道工序则整颗完工；不合格则停留在该工序等待处理。</summary>
    Task<Result<SerialNumberDetailDto>> TrackOutAsync(
        string sn,
        SnTrackOutRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SerialNumberDetailDto>> ScrapAsync(
        string sn,
        string? remark = null,
        CancellationToken cancellationToken = default);
}
