using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 主数据 CSV 导入导出：导出内容正确、导入按编码 upsert、非法资源被拒绝。
/// </summary>
[Collection(ApiCollection.Name)]
public class CatalogCsvTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 导出产品_返回CSV且包含表头与已存在数据()
    {
        var admin = await LoginAsync();
        var code = NewCode("P");
        await CreateProductAsync(admin, code, "导出测试产品");

        var response = await admin.GetAsync("/api/master-data/products/export");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("code,name,spec,unit,remark,isActive", content);
        Assert.Contains(code, content);
    }

    [Fact]
    public async Task 导入产品_按编码upsert_新建与更新分别计数()
    {
        var admin = await LoginAsync();

        var existingCode = NewCode("P");
        await CreateProductAsync(admin, existingCode, "导入前名称");
        var newCode = NewCode("P");

        var csv = string.Join(
            "\n",
            "code,name,spec,unit,remark,isActive",
            $"{existingCode},导入后名称,SPEC-NEW,PCS,来自CSV,1",
            $"{newCode},全新产品,,PCS,,1");

        var response = await admin.PostAsJsonAsync("/api/master-data/products/import", new { content = csv });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("updated").GetInt32());
        Assert.Equal(0, result.GetProperty("errors").GetArrayLength());

        // 校验更新确实生效
        var updated = await admin.GetFromJsonAsync<JsonElement>($"/api/master-data/products?keyword={existingCode}");
        var item = updated.GetProperty("items").EnumerateArray().First();
        Assert.Equal("导入后名称", item.GetProperty("name").GetString());
        Assert.Equal("SPEC-NEW", item.GetProperty("spec").GetString());
    }

    [Fact]
    public async Task 导入产品_存在非法行时返回逐行错误但不影响其他行()
    {
        var admin = await LoginAsync();
        var validCode = NewCode("P");

        var csv = string.Join(
            "\n",
            "code,name,spec,unit,remark,isActive",
            $",缺少编码的产品,,,",
            $"{validCode},正常产品,,PCS,,1");

        var response = await admin.PostAsJsonAsync("/api/master-data/products/import", new { content = csv });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(1, result.GetProperty("errors").GetArrayLength());
        Assert.Contains("第 2 行", result.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task 不支持的主数据_导出返回400()
    {
        var admin = await LoginAsync();

        var response = await admin.GetAsync("/api/master-data/boms/export");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 导出工作中心_表头与枚举字段正确()
    {
        var admin = await LoginAsync();

        var response = await admin.GetAsync("/api/master-data/work-centers/export");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("code,name,type,workshop,remark,isActive", content);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private static async Task CreateProductAsync(HttpClient admin, string code, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code,
            name,
            spec = (string?)null,
            unit = "PCS",
            remark = (string?)null,
        });
        response.EnsureSuccessStatusCode();
    }

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
