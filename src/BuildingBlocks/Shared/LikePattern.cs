namespace QiaoMES.Shared;

/// <summary>
/// SQL LIKE / ILIKE 模式转义。
/// <para>把用户输入中的 <c>\</c>、<c>%</c>、<c>_</c> 转义，避免被当成通配符。</para>
/// </summary>
public static class LikePattern
{
    /// <summary>转义字符，需与查询时传入的 escape 参数一致。</summary>
    public const string EscapeCharacter = "\\";

    /// <summary>构造「包含」匹配模式（<c>%关键字%</c>）。</summary>
    public static string Contains(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
