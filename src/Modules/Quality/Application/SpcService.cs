using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Application;

/// <summary>
/// SPC（统计过程控制）基础能力：按检验项汇总趋势，并做基础判异。
/// </summary>
public interface ISpcService
{
    Task<Result<SpcTrendDto>> GetTrendAsync(SpcTrendRequest request, CancellationToken cancellationToken = default);
}

public class SpcService(IInspectionRepository repository) : ISpcService
{
    /// <summary>连续同侧点数阈值（经典判异准则之一）。</summary>
    private const int RunLengthThreshold = 7;

    public async Task<Result<SpcTrendDto>> GetTrendAsync(
        SpcTrendRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ItemName))
        {
            return Result.Failure<SpcTrendDto>(Error.Validation("Spc.InvalidItemName", "检验项名称不能为空"));
        }

        var points = request.Points is < 5 or > 200 ? 30 : request.Points;
        var samples = await repository.GetItemHistoryAsync(request.ItemName.Trim(), request.From, request.To, points, cancellationToken);

        if (samples.Count == 0)
        {
            return Result.Success(new SpcTrendDto(
                request.ItemName, null, null, null, null, null, null, 0, false, "该检验项暂无数值型数据", []));
        }

        var values = samples.Where(s => s.Value is not null).Select(s => (double)s.Value!.Value).ToList();
        double? mean = null;
        double? stdDev = null;
        double? ucl = null;
        double? lcl = null;
        var hasSignal = false;
        var signalDescription = string.Empty;

        if (values.Count >= 2)
        {
            mean = values.Average();
            var variance = values.Sum(v => (v - mean.Value) * (v - mean.Value)) / (values.Count - 1);
            stdDev = Math.Sqrt(variance);
            ucl = mean + 3 * stdDev;
            lcl = mean - 3 * stdDev;

            // 判异 1：单点超出 3σ 控制限
            var outOfControlIndex = values.FindIndex(v => v > ucl.Value || v < lcl.Value);
            if (outOfControlIndex >= 0)
            {
                hasSignal = true;
                signalDescription = $"第 {outOfControlIndex + 1} 个点超出 3σ 控制限（{values[outOfControlIndex]:0.###}）";
            }
            else
            {
                // 判异 2：连续 N 点落在均值同侧
                var run = FindSameSideRun(values, mean.Value);
                if (run >= RunLengthThreshold)
                {
                    hasSignal = true;
                    signalDescription = $"连续 {run} 个点位于均值同一侧，过程可能发生偏移";
                }
            }
        }

        var dto = new SpcTrendDto(
            request.ItemName,
            null,
            null,
            ToDecimal(mean),
            ToDecimal(stdDev),
            ToDecimal(ucl),
            ToDecimal(lcl),
            values.Count,
            hasSignal,
            signalDescription.Length > 0 ? signalDescription : null,
            samples
                .Select(s => new SpcPointDto(s.Timestamp, s.Value, s.IsQualified, s.InspectionNumber))
                .ToList());

        return Result.Success(dto);
    }

    /// <summary>返回最长的一段「连续位于均值同一侧」的点数。</summary>
    private static int FindSameSideRun(IReadOnlyList<double> values, double mean)
    {
        var maxRun = 0;
        var currentRun = 0;
        var sideAbove = values.Count > 0 && values[0] >= mean;

        foreach (var value in values)
        {
            var isAbove = value >= mean;
            if (isAbove == sideAbove)
            {
                currentRun++;
            }
            else
            {
                sideAbove = isAbove;
                currentRun = 1;
            }

            maxRun = Math.Max(maxRun, currentRun);
        }

        return maxRun;
    }

    private static decimal? ToDecimal(double? value)
        => value is null ? null : Math.Round((decimal)value.Value, 4);
}
