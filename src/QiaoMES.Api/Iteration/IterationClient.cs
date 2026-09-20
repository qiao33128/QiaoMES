using System.Text.Json;

namespace QiaoMES.Api.Iteration;

/// <summary>调用结果。<b>用返回值表达失败，不抛异常</b>（与项目内大模型客户端同一约定）：调用方要能区分「服务没配」「服务不可达」「服务说不行」并给出不同提示。</summary>
public sealed record IterationResult(bool Ok, JsonElement Payload, string? Error)
{
    public static IterationResult Success(JsonElement payload) => new(true, payload, null);

    public static IterationResult Failure(string error) => new(false, default, error);
}

/// <summary>提建议的请求体（宿主侧）。</summary>
public sealed record SuggestionRequest(
    string Title,
    string Body,
    string Category,
    string? ActorId,
    string? ActorName,
    string? SourceFeature,
    /// <summary>这次建议针对哪个功能 —— 传的是**该页面的鉴权权限码**（如 workorders:read）。后端据此校验"只能提自己可用的功能"。</summary>
    string? SourcePermission);

/// <summary>审阅请求体（宿主侧）。</summary>
public sealed record ReviewRequest(bool? Approve, bool? Reject, string? Note, string? Actor);

/// <summary>
/// 迭代服务的客户端。<br/>
/// 只做**转发 + 密钥代持**，不解释服务的内部结构（前端拿到的就是服务返回的原始结构）——
/// 这样迭代服务演进（加字段、加状态）时宿主不用跟着改。
/// </summary>
public sealed class IterationClient(HttpClient http, IterationOptions options, ILogger<IterationClient> logger)
{
    public bool IsConfigured => options.IsConfigured;

    public Task<IterationResult> GetCycleAsync(CancellationToken ct)
        => SendAsync(HttpMethod.Get, "/api/cycles/current", null, ct);

    public Task<IterationResult> SubmitAsync(SuggestionRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/api/suggestions", request, ct);

    public Task<IterationResult> ReviewAsync(Guid itemId, ReviewRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/plan-items/{itemId}/review", request, ct);

    public Task<IterationResult> AdvanceAsync(string action, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/cycles/current/{action}", null, ct);

    private async Task<IterationResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return IterationResult.Failure(
                "迭代服务还没配置：请在 QiaoMES 的配置里填上 Iteration:BaseUrl（以及需要的 Iteration:AdminKey）。");
        }

        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (!string.IsNullOrWhiteSpace(options.AdminKey))
        {
            request.Headers.Add("X-Admin-Key", options.AdminKey);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 900)));

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                // 尽量把服务端的说明原样带给前端：这里的报错往往就是用户最需要看到的那句话
                return IterationResult.Failure(
                    $"迭代服务返回 {(int)response.StatusCode}：{ReadDetail(text)}");
            }

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            return IterationResult.Success(document.RootElement.Clone());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return IterationResult.Failure(
                $"迭代服务响应超时（>{options.TimeoutSeconds}s）。提交建议要调大模型做评审与一致性检查，偶尔会久一点，可重试。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "调用迭代服务失败：{Method} {Path}", method, path);
            return IterationResult.Failure($"迭代服务不可达：{ex.Message}");
        }
    }

    /// <summary>优先取 ProblemDetails 的 detail/title，取不到就把原文截断返回，不吞掉真正的原因。</summary>
    private static string ReadDetail(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "(响应体为空)";
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            foreach (var name in new[] { "detail", "title", "error", "message" })
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var detail = value.GetString();
                    if (!string.IsNullOrWhiteSpace(detail))
                    {
                        return detail;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 不是 JSON（比如网关返回的 HTML），走下面的截断返回
        }

        return text.Length <= 400 ? text : text[..400] + "…";
    }
}
