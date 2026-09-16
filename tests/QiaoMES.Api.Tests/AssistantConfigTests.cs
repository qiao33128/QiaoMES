using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 智能问数的「模型配置」入口（页面上可配、保存即生效）。
/// <para>
/// 这里最要紧的两条：<b>接口绝不回传密钥明文</b>、<b>保存后立刻生效</b>（不需要重启）。
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class AssistantConfigTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 读取配置_不回传密钥明文()
    {
        var admin = await LoginAsync();

        var response = await admin.GetAsync("/api/assistant/config");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // 安全回归：响应里不允许出现 apiKey 明文，只允许掩码与"是否已配置"
        Assert.False(json.TryGetProperty("apiKey", out _));
        Assert.True(json.TryGetProperty("hasApiKey", out _));
        Assert.True(json.TryGetProperty("apiKeyMasked", out _));

        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("baseUrl").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("model").GetString()));
        Assert.True(json.GetProperty("maxRows").GetInt32() > 0);

        var source = json.GetProperty("source").GetString();
        Assert.True(source is "database" or "configuration", $"意外的 source：{source}");
    }

    [Fact]
    public async Task 保存配置_落库并立即生效()
    {
        var admin = await LoginAsync();
        var origin = await ReadConfigAsync(admin);
        var uniqueModel = $"qiam-test-{Guid.NewGuid():N}"[..22];

        try
        {
            var saved = await admin.PutAsJsonAsync(
                "/api/assistant/config", new { model = uniqueModel, maxRows = 321 });
            saved.EnsureSuccessStatusCode();

            var savedBody = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(uniqueModel, savedBody.GetProperty("model").GetString());
            Assert.Equal(321, savedBody.GetProperty("maxRows").GetInt32());

            // 回读：已经落到数据库（而不是只在内存里）
            var reread = await ReadConfigAsync(admin);
            Assert.Equal(uniqueModel, reread.GetProperty("model").GetString());
            Assert.Equal("database", reread.GetProperty("source").GetString());

            // 立即生效：/status 读的是运行时配置，没有重启也应该反映新值
            var status = await admin.GetFromJsonAsync<JsonElement>("/api/assistant/status");
            Assert.Equal(321, status.GetProperty("maxRows").GetInt32());
        }
        finally
        {
            // 还原：整套集成测试共用一个真实库，不能把模型名留给后面的用例
            await admin.PutAsJsonAsync("/api/assistant/config", new
            {
                model = origin.GetProperty("model").GetString(),
                maxRows = origin.GetProperty("maxRows").GetInt32(),
            });
        }
    }

    [Fact]
    public async Task 非法模型地址_被拒绝()
    {
        var admin = await LoginAsync();

        var response = await admin.PutAsJsonAsync("/api/assistant/config", new { baseUrl = "这不是一个URL" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 超出行数上限的配置_被拒绝()
    {
        var admin = await LoginAsync();

        var response = await admin.PutAsJsonAsync("/api/assistant/config", new { maxRows = 99999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 测试连接_返回结构与可读消息()
    {
        var admin = await LoginAsync();

        var response = await admin.PostAsync("/api/assistant/config/test", null);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("ok", out var ok));
        Assert.Contains(ok.ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("message").GetString()));
    }

    private static async Task<JsonElement> ReadConfigAsync(HttpClient client)
        => await client.GetFromJsonAsync<JsonElement>("/api/assistant/config");

    private async Task<HttpClient> LoginAsync()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = AdminUserName, password = AdminPassword });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }
}
