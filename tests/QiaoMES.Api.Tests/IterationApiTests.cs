using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 改进建议入口（宿主侧）。这里只验**宿主自己的职责**：
/// 权限闸门、功能范围校验、以及迭代服务没配 / 不可达时给出可读提示。
/// <para>
/// 真正的 AI 评审、一致性检查、周期结算与执行都在 AI 迭代服务里（那是另一个项目），
/// 宿主对这些逻辑**不做任何假设** —— 它只是转发，所以宿主测试不该去断言服务的行为。
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class IterationApiTests(QiaoMESApiFactory factory)
{
    private async Task<HttpClient> LoginAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin123!" });
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = payload.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task 功能范围不是已登记的权限码_直接拒绝()
    {
        var client = await LoginAsync();

        var response = await client.PostAsJsonAsync("/api/iteration/suggestions", new
        {
            title = "随便改点什么",
            body = "测试用",
            category = "Modify",
            sourcePermission = "not-a-real-permission",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("功能范围", text);
    }

    [Fact]
    public async Task 未配置迭代服务时_给出明确提示而不是无法解释的失败()
    {
        var client = await LoginAsync();

        var response = await client.GetAsync("/api/iteration/cycle");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("Iteration:BaseUrl", text);
    }
}
