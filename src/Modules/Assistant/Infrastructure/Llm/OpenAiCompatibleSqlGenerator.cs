using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QiaoMES.Assistant.Application;
using QiaoMES.Assistant.Application.Contracts;
using QiaoMES.Assistant.Domain;

namespace QiaoMES.Assistant.Infrastructure.Llm;

/// <summary>
/// 自然语言 → SQL 的模型适配层,走 <b>OpenAI 兼容</b> 的 <c>/chat/completions</c>。
/// <para>
/// 刻意不绑定某一家:DeepSeek、通义千问、Kimi、硅基流动、vLLM、本地 Ollama 都提供这一接口,
/// 换模型只改 <c>Assistant:Llm:*</c> 三行配置,代码零改动。
/// </para>
/// </summary>
public sealed class OpenAiCompatibleSqlGenerator(
    HttpClient httpClient,
    AssistantOptions options,
    ILogger<OpenAiCompatibleSqlGenerator> logger) : ISqlGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const string SystemPrompt = """
        你是 QiaoMES(制造执行系统)的数据分析助手。你的唯一任务:把用户的中文业务问题,
        翻译成 **一条可在 PostgreSQL 上直接执行的只读查询**,并给出最合适的展示方式。

        ## 硬性约束(违反即失败)
        1. 只能输出**一条** SELECT(允许 WITH ... SELECT);禁止 INSERT/UPDATE/DELETE/DDL/多语句/分号/SQL 注释。
        2. 表名必须写全限定名(schema.table),并给别名。
        3. 列名必须用双引号包裹(本库列名是 PascalCase,不加引号会被折叠成小写而报错)。
        4. 每张业务表都要过滤软删除:`AND 别名."IsDeleted" = false`。
        5. 不要写 LIMIT(系统会统一加结果集上界)。
        6. 结果列用 `AS "中文列名"`,让结果可以直接给人看。
        7. 只能使用「语义层」里出现过的表和列,不要臆造字段。

        ## 输出格式(必须是严格的 JSON,不要包 markdown 代码块)
        {"thought":"一句话思路","sql":"SELECT ...","chart":"table|bar|line|pie","xField":"列名","yField":"数值列名","explanation":"一句话结论或口径说明"}

        如果这个问题用现有数据确实回答不了(例如问了库里没有的维度),输出:
        {"sql":null,"explanation":"说明为什么答不了,以及可以改成问什么"}
        """;

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(options.Llm.BaseUrl)
           && !string.IsNullOrWhiteSpace(options.Llm.Model)
           && (!string.IsNullOrWhiteSpace(options.Llm.ApiKey) || IsLocalEndpoint(options.Llm.BaseUrl));

    public string Model => options.Llm.Model;

    public Task<SqlGenerationResult> GenerateAsync(SqlGenerationRequest request, CancellationToken cancellationToken = default)
        => AskAsync(BuildUserPrompt(request), cancellationToken);

    public Task<SqlGenerationResult> RepairAsync(
        SqlGenerationRequest request,
        string failedSql,
        string error,
        CancellationToken cancellationToken = default)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine(BuildUserPrompt(request));
        prompt.AppendLine();
        prompt.AppendLine("## 修正要求");
        prompt.AppendLine("你上一次给出的 SQL 没能执行成功,请结合报错修正后,**重新输出完整的 JSON**。");
        prompt.AppendLine();
        prompt.AppendLine("上一次的 SQL:");
        prompt.AppendLine("```sql");
        prompt.AppendLine(failedSql);
        prompt.AppendLine("```");
        prompt.AppendLine();
        prompt.AppendLine("错误信息:");
        prompt.AppendLine(error.Length > 1200 ? error[..1200] : error);

        return AskAsync(prompt.ToString(), cancellationToken);
    }

    private static string BuildUserPrompt(SqlGenerationRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("# 数据库语义层");
        prompt.AppendLine(request.SchemaText);
        prompt.AppendLine("# 业务口径");
        prompt.AppendLine(request.GlossaryText);
        prompt.AppendLine("# 用户问题");
        prompt.AppendLine(request.Question);

        if (!string.IsNullOrWhiteSpace(request.PreviousSql))
        {
            prompt.AppendLine();
            prompt.AppendLine("# 对话上下文(本次可能是追问,可在上一版 SQL 基础上调整)");
            prompt.AppendLine($"上一轮问题:{request.PreviousQuestion}");
            prompt.AppendLine("上一轮 SQL:");
            prompt.AppendLine("```sql");
            prompt.AppendLine(request.PreviousSql);
            prompt.AppendLine("```");
        }

        return prompt.ToString();
    }

    private async Task<SqlGenerationResult> AskAsync(string userPrompt, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return SqlGenerationResult.Failed("未配置大模型(Assistant:Llm:BaseUrl / ApiKey / Model)");
        }

        var payload = new
        {
            model = options.Llm.Model,
            temperature = options.Llm.Temperature,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = userPrompt },
            },
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(options.Llm.ApiKey))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Llm.ApiKey);
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.Llm.TimeoutSeconds, 10, 600)));

            using var response = await httpClient.SendAsync(httpRequest, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var detail = body.Length > 500 ? body[..500] : body;
                logger.LogWarning("大模型调用失败 {Status}:{Body}", (int)response.StatusCode, detail);
                return SqlGenerationResult.Failed($"大模型调用失败(HTTP {(int)response.StatusCode}):{detail}");
            }

            var content = ExtractContent(body);
            if (string.IsNullOrWhiteSpace(content))
            {
                return SqlGenerationResult.Failed("大模型返回内容为空");
            }

            return Parse(content);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SqlGenerationResult.Failed($"大模型调用超时(超过 {options.Llm.TimeoutSeconds} 秒)");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "调用大模型异常");
            return SqlGenerationResult.Failed($"大模型调用异常:{exception.Message}");
        }
    }

    private Uri BuildEndpoint()
    {
        var baseUrl = options.Llm.BaseUrl.Trim().TrimEnd('/');
        return new Uri(baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions");
    }

    /// <summary>从 OpenAI 兼容响应里取出 <c>choices[0].message.content</c>(兼容个别厂商的 <c>text</c> 字段)。</summary>
    private static string? ExtractContent(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            // Ollama 原生 /api/chat 的响应形状:{"message":{"content":"..."}}
            if (document.RootElement.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var nativeContent))
            {
                return nativeContent.GetString();
            }

            return null;
        }

        var first = choices[0];
        if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
        {
            return content.GetString();
        }

        return first.TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    /// <summary>宽松解析:模型偶尔会包 ```json 代码块或加前后解释,这里只截取第一个 <c>{</c> 到最后一个 <c>}</c>。</summary>
    private SqlGenerationResult Parse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return SqlGenerationResult.Failed($"大模型输出无法解析为 JSON:{Truncate(content, 300)}");
        }

        LlmAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<LlmAnswer>(content[start..(end + 1)], JsonOptions);
        }
        catch (JsonException exception)
        {
            return SqlGenerationResult.Failed($"大模型输出 JSON 解析失败:{exception.Message}");
        }

        if (answer is null)
        {
            return SqlGenerationResult.Failed("大模型输出为空对象");
        }

        var chart = answer.Chart?.Trim().ToLowerInvariant() switch
        {
            "bar" or "line" or "pie" or "table" => answer.Chart.Trim().ToLowerInvariant(),
            _ => "table",
        };

        return new SqlGenerationResult(
            string.IsNullOrWhiteSpace(answer.Sql) ? null : answer.Sql.Trim(),
            chart,
            answer.XField,
            answer.YField,
            answer.Explanation,
            answer.Thought,
            options.Llm.Model);
    }

    private static bool IsLocalEndpoint(string baseUrl)
        => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
           && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));

    private static string Truncate(string value, int length)
        => value.Length <= length ? value : value[..length] + "…";

    private sealed record LlmAnswer(
        string? Thought,
        string? Sql,
        string? Chart,
        string? XField,
        string? YField,
        string? Explanation);
}
