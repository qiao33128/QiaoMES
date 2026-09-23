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
public sealed class IterationClient(
    HttpClient http,
    IterationSettingsFile settings,
    IterationOptions options,
    ILogger<IterationClient> logger)
{
    public bool IsConfigured => settings.Read().IsConfigured;

    public Task<IterationResult> GetCycleAsync(CancellationToken ct)
        => SendAsync(HttpMethod.Get, "/api/cycles/current", null, ct);

    public Task<IterationResult> SubmitAsync(SuggestionRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, "/api/suggestions", request, ct);

    public Task<IterationResult> ReviewAsync(Guid itemId, ReviewRequest request, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/plan-items/{itemId}/review", request, ct);

    public Task<IterationResult> AdvanceAsync(string action, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"/api/cycles/current/{action}", null, ct);

    /// <summary>
    /// 「测试连接」：调迭代服务的就绪接口（<c>GET /health/ready</c>，**只读无副作用**）。<para>
    /// 能确认的：地址是否可达、服务端是否配了模型与管理员密钥、宿主这一侧密钥是否为空。<br/>
    /// 不能确认的：密钥**值**是否与对面一致 —— 迭代服务的管理员接口全是 POST 且都有副作用
    /// （冻结周期 / 批准条目 / 领任务），拿它们做探活会真的改数据，所以这里不做，
    /// 如实告诉用户「密钥对不对要用一次真实的管理员操作来验证」，而不是给一个假的"全部正常"。
    /// </para>
    /// </summary>
    public async Task<IterationProbeResult> ProbeAsync(CancellationToken ct)
    {
        var current = settings.Read();

        if (!current.IsConfigured)
        {
            return new IterationProbeResult(
                false, "还没填迭代服务地址 —— 先在上面的「迭代服务配置」里填上地址并保存。");
        }

        var result = await SendAsync(HttpMethod.Get, "/health/ready", null, ct);
        if (!result.Ok)
        {
            return new IterationProbeResult(false, result.Error ?? "迭代服务不可达");
        }

        var payload = result.Payload;
        var parts = new List<string> { $"服务可达（{current.BaseUrl}）" };

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
            {
                parts.Add($"模型 {model.GetString()}");
            }

            if (payload.TryGetProperty("workspaceCount", out var workspaces) && workspaces.TryGetInt32(out var count))
            {
                parts.Add($"已注册工作区 {count} 个");
            }
        }

        var summary = string.Join("，", parts);

        var adminConfigured = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("adminConfigured", out var flag)
            && flag.ValueKind == JsonValueKind.True;

        if (!adminConfigured)
        {
            return new IterationProbeResult(false,
                $"{summary}。但**迭代服务自己没配管理员密钥**（Admin__ApiKey）—— 所有管理员操作都会被拒绝，请先在迭代服务那边配上。");
        }

        if (string.IsNullOrWhiteSpace(current.AdminKey))
        {
            return new IterationProbeResult(false,
                $"{summary}。但宿主侧的管理员密钥是空的 —— 审阅 / 批准 / 推进周期会失败（提交修改建议不受影响）。");
        }

        return new IterationProbeResult(true,
            $"{summary}，两端的管理员密钥都已配置。密钥值是否一致，需要用一次真实的管理员操作（例如批准一条计划）来验证。");
    }

    private async Task<IterationResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        // 每次请求都现读配置：页面上保存完，下一个请求就生效（不需要重启，也不需要缓存失效通知）
        var current = settings.Read();

        if (!current.IsConfigured)
        {
            // 生产环境这里就是一句"还没配置"；开发环境会在自己的配置里补一句说明 ——
            // 它**刻意**不接自迭代服务（数据隔离），照着这句提示去填地址反而有害。
            var hint = string.IsNullOrWhiteSpace(options.UnconfiguredHint)
                ? string.Empty
                : $"{Environment.NewLine}{options.UnconfiguredHint}";

            return IterationResult.Failure(
                "迭代服务还没配置：请在「改进建议 → 迭代服务配置」里填上地址并保存（也可以走部署配置 Iteration:BaseUrl / Iteration:AdminKey）。"
                + hint);
        }

        using var request = new HttpRequestMessage(method, Resolve(current.BaseUrl, path));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (!string.IsNullOrWhiteSpace(current.AdminKey))
        {
            request.Headers.Add("X-Admin-Key", current.AdminKey);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(current.TimeoutSeconds, 5, 900)));

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
                $"迭代服务响应超时（>{current.TimeoutSeconds}s）。提交建议要调大模型做评审与一致性检查，偶尔会久一点，可重试。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "调用迭代服务失败：{Method} {Path}", method, path);
            return IterationResult.Failure($"迭代服务不可达：{ex.Message}");
        }
    }

    /// <summary>
    /// 每次请求都现算绝对地址。<para>
    /// 🔴 不能靠 <c>HttpClient.BaseAddress</c>：那个是**启动时定型**的，
    /// 而地址现在可以在页面上改（保存即生效）—— 用 BaseAddress 会出现「页面提示保存成功，请求却还打向老地址」，
    /// 是最难查的那类"配置看起来生效了其实没有"。
    /// </para>
    /// </summary>
    private static Uri Resolve(string baseUrl, string path)
        => new(new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute), path.TrimStart('/'));

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
