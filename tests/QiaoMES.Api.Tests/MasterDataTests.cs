using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 主数据模块端到端验证：编码唯一、关键字查询、启停、权限与入参校验。
/// </summary>
[Collection(ApiCollection.Name)]
public class MasterDataTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 未登录访问产品列表_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/master-data/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 创建产品_成功且能按关键字查询到()
    {
        var admin = await LoginAsync();
        var code = NewCode("P");

        var createResponse = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code,
            name = "集成测试产品",
            spec = "SPEC-A",
            unit = "PCS",
            remark = (string?)null,
        });
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, created.GetProperty("code").GetString());
        Assert.True(created.GetProperty("isActive").GetBoolean());

        var list = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/master-data/products?keyword={code}&page=1&pageSize=10");

        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task 产品编码重复_返回409()
    {
        var admin = await LoginAsync();
        var code = NewCode("P");
        var payload = new
        {
            code,
            name = "重复编码产品",
            spec = (string?)null,
            unit = (string?)null,
            remark = (string?)null,
        };

        var first = await admin.PostAsJsonAsync("/api/master-data/products", payload);
        first.EnsureSuccessStatusCode();

        var second = await admin.PostAsJsonAsync("/api/master-data/products", payload);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task 停用产品_状态变为停用()
    {
        var admin = await LoginAsync();
        var createResponse = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code = NewCode("P"),
            name = "待停用产品",
            spec = (string?)null,
            unit = (string?)null,
            remark = (string?)null,
        });
        createResponse.EnsureSuccessStatusCode();
        var id = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await admin.PutAsJsonAsync($"/api/master-data/products/{id}/status", new { isActive = false });
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(updated.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task 创建工序_标准工时为负_返回400()
    {
        var admin = await LoginAsync();

        var response = await admin.PostAsJsonAsync("/api/master-data/operations", new
        {
            code = NewCode("OP"),
            name = "非法工时工序",
            standardSeconds = -1,
            isKeyOperation = false,
            defaultWorkCenterId = (Guid?)null,
            remark = (string?)null,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 创建工作中心与物料_均成功()
    {
        var admin = await LoginAsync();

        var workCenter = await admin.PostAsJsonAsync("/api/master-data/work-centers", new
        {
            code = NewCode("WC"),
            name = "SMT 一线",
            type = 0,
            workshop = "古城一车间",
            parentId = (Guid?)null,
            remark = (string?)null,
        });
        workCenter.EnsureSuccessStatusCode();

        var material = await admin.PostAsJsonAsync("/api/master-data/materials", new
        {
            code = NewCode("M"),
            name = "贴片电阻 10K",
            materialType = 0,
            supplierPartNumber = "SUP-001",
            spec = "0603",
            unit = "PCS",
            remark = (string?)null,
        });
        material.EnsureSuccessStatusCode();

        var materialBody = await material.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, materialBody.GetProperty("materialType").GetInt32());
    }

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

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
