using System.Text;

namespace QiaoMES.MasterData.Application;

/// <summary>
/// 极简 CSV 读写（支持引号转义与字段内换行），避免为导入导出引入额外依赖。
/// </summary>
internal static class CsvSerializer
{
    /// <summary>把一个字段按 CSV 规则转义。</summary>
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r'))
        {
            return $"\"{text.Replace("\"", "\"\"")}\"";
        }

        return text;
    }

    /// <summary>拼接一行。</summary>
    public static string WriteLine(IEnumerable<string?> values)
        => string.Join(",", values.Select(Escape));

    /// <summary>解析 CSV 文本为行 × 列。</summary>
    public static List<List<string>> Parse(string content)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < content.Length; index++)
        {
            var current = content[index];

            if (inQuotes)
            {
                if (current == '"')
                {
                    // 连续两个引号表示一个引号字符
                    if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            switch (current)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(current);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
