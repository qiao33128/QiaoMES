using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// BOM 与工艺路线端到端验证：版本化规则（同产品只有一个生效版本）、明细替换、生效版本不可删除、步骤归一化。
/// </summary>
[Collection(ApiCollection.Name)]
public class BomAndRoutingTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 创建BOM_明细带出物料编码且应领用量含损耗()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var materialId = await CreateMaterialAsync(admin, "M-BOM-");

        var response = await admin.PostAsJsonAsync("/api/master-data/boms", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            items = new[]
            {
                new { materialId, quantity = 2.5m, unit = "PCS", lossRate = 0.01m, remark = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("V1.0", body.GetProperty("version").GetString());
        Assert.Equal(1, body.GetProperty("itemCount").GetInt32());
        Assert.False(body.GetProperty("isActive").GetBoolean());

        var item = body.GetProperty("items").EnumerateArray().First();
        Assert.False(string.IsNullOrEmpty(item.GetProperty("materialCode").GetString()));
        Assert.Equal(2.525m, item.GetProperty("requiredQuantity").GetDecimal());
    }

    [Fact]
    public async Task 同一产品只能有一个生效BOM_激活新版本后旧版本自动失效()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var materialId = await CreateMaterialAsync(admin, "M-BOM2-");

        var firstId = await CreateBomAsync(admin, productId, "V1.0", materialId);
        var secondId = await CreateBomAsync(admin, productId, "V2.0", materialId);

        var activateFirst = await admin.PostAsync($"/api/master-data/boms/{firstId}/activate", null);
        activateFirst.EnsureSuccessStatusCode();

        var activateSecond = await admin.PostAsync($"/api/master-data/boms/{secondId}/activate", null);
        activateSecond.EnsureSuccessStatusCode();

        var first = await admin.GetFromJsonAsync<JsonElement>($"/api/master-data/boms/{firstId}");
        var second = await admin.GetFromJsonAsync<JsonElement>($"/api/master-data/boms/{secondId}");

        Assert.False(first.GetProperty("isActive").GetBoolean());
        Assert.True(second.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task 更新BOM明细_条数与内容同步变化()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var materialA = await CreateMaterialAsync(admin, "M-BOM3A-");
        var materialB = await CreateMaterialAsync(admin, "M-BOM3B-");

        var bomId = await CreateBomAsync(admin, productId, "V1.0", materialA);

        var response = await admin.PutAsJsonAsync($"/api/master-data/boms/{bomId}", new
        {
            remark = "替换明细",
            items = new[]
            {
                new { materialId = materialB, quantity = 1m, unit = "PCS", lossRate = 0m, remark = (string?)null },
                new { materialId = materialA, quantity = 3m, unit = "PCS", lossRate = 0m, remark = (string?)"备用" },
            },
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("itemCount").GetInt32());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.Equal("替换明细", body.GetProperty("remark").GetString());
    }

    [Fact]
    public async Task 生效BOM不允许删除_返回409()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var materialId = await CreateMaterialAsync(admin, "M-BOM4-");
        var bomId = await CreateBomAsync(admin, productId, "V1.0", materialId);

        (await admin.PostAsync($"/api/master-data/boms/{bomId}/activate", null)).EnsureSuccessStatusCode();

        var response = await admin.DeleteAsync($"/api/master-data/boms/{bomId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task 创建工艺路线_步骤顺序归一化为10的倍数()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var operationA = await CreateOperationAsync(admin, "OP-A-");
        var operationB = await CreateOperationAsync(admin, "OP-B-");

        var response = await admin.PostAsJsonAsync("/api/master-data/routings", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            steps = new object[]
            {
                new { sequence = 2, operationId = operationB, workCenterId = (Guid?)null, standardSeconds = 30, isQualityGate = true },
                new { sequence = 1, operationId = operationA, workCenterId = (Guid?)null, standardSeconds = 60, isQualityGate = false },
            },
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var steps = body.GetProperty("steps").EnumerateArray().ToList();

        Assert.Equal(2, steps.Count);
        // 归一化为 10、20，且按顺序排列
        Assert.Equal(10, steps[0].GetProperty("sequence").GetInt32());
        Assert.Equal(20, steps[1].GetProperty("sequence").GetInt32());
        Assert.Equal(90, body.GetProperty("totalStandardSeconds").GetInt32());
    }

    [Fact]
    public async Task 工艺路线重复工序_返回400()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var operationId = await CreateOperationAsync(admin, "OP-DUP-");

        var response = await admin.PostAsJsonAsync("/api/master-data/routings", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            steps = new object[]
            {
                new { sequence = 1, operationId, workCenterId = (Guid?)null, standardSeconds = 10, isQualityGate = false },
                new { sequence = 2, operationId, workCenterId = (Guid?)null, standardSeconds = 10, isQualityGate = false },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 生效工艺路线版本唯一()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var operationId = await CreateOperationAsync(admin, "OP-ACT-");

        var firstId = await CreateRoutingAsync(admin, productId, "V1.0", operationId);
        var secondId = await CreateRoutingAsync(admin, productId, "V2.0", operationId);

        (await admin.PostAsync($"/api/master-data/routings/{firstId}/activate", null)).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/master-data/routings/{secondId}/activate", null)).EnsureSuccessStatusCode();

        var first = await admin.GetFromJsonAsync<JsonElement>($"/api/master-data/routings/{firstId}");
        var second = await admin.GetFromJsonAsync<JsonElement>($"/api/master-data/routings/{secondId}");

        Assert.False(first.GetProperty("isActive").GetBoolean());
        Assert.True(second.GetProperty("isActive").GetBoolean());
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<Guid> CreateProductAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code = NewCode("P"),
            name = "BOM 测试产品",
            spec = (string?)null,
            unit = "PCS",
            remark = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateMaterialAsync(HttpClient admin, string prefix)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/materials", new
        {
            code = NewCode(prefix),
            name = "测试物料",
            materialType = 0,
            supplierPartNumber = (string?)null,
            spec = "0603",
            unit = "PCS",
            remark = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateOperationAsync(HttpClient admin, string prefix)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/operations", new
        {
            code = NewCode(prefix),
            name = "测试工序",
            standardSeconds = 60,
            isKeyOperation = false,
            defaultWorkCenterId = (Guid?)null,
            remark = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBomAsync(HttpClient admin, Guid productId, string version, Guid materialId)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/boms", new
        {
            productId,
            version,
            remark = (string?)null,
            items = new[]
            {
                new { materialId, quantity = 1m, unit = "PCS", lossRate = 0m, remark = (string?)null },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateRoutingAsync(HttpClient admin, Guid productId, string version, Guid operationId)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/routings", new
        {
            productId,
            version,
            remark = (string?)null,
            steps = new object[]
            {
                new { sequence = 1, operationId, workCenterId = (Guid?)null, standardSeconds = 60, isQualityGate = false },
            },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
