using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 工单按工艺路线展开工序 + 工序级报工 的端到端验证。
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkOrderOperationTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 产品没有生效工艺路线_下达工单返回409()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        var workOrderId = await CreateWorkOrderAsync(admin, productId);

        var response = await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task 下达工单_按生效工艺路线展开工序任务()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        await CreateActiveRoutingAsync(admin, productId, ["印刷", "贴片"], ["OP-A-", "OP-B-"]);
        var workOrderId = await CreateWorkOrderAsync(admin, productId);

        var response = await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operations = body.GetProperty("operations").EnumerateArray().ToList();

        Assert.Equal(2, operations.Count);
        Assert.Equal(10, operations[0].GetProperty("sequence").GetInt32());
        Assert.Equal(20, operations[1].GetProperty("sequence").GetInt32());
        Assert.False(string.IsNullOrEmpty(operations[0].GetProperty("operationCode").GetString()));
        Assert.Equal(0, operations[0].GetProperty("status").GetInt32());

        // 快照了工艺路线版本
        Assert.False(string.IsNullOrEmpty(body.GetProperty("routingVersion").GetString()));
    }

    [Fact]
    public async Task 工序报工_跳序被拒绝_按序完成后工单自动完成()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        await CreateActiveRoutingAsync(admin, productId, ["印刷", "贴片"], ["OP-C-", "OP-D-"]);
        var workOrderId = await CreateWorkOrderAsync(admin, productId, plannedQuantity: 10);

        var released = await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null);
        released.EnsureSuccessStatusCode();
        var operations = (await released.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("operations").EnumerateArray().ToList();
        var firstOperationId = operations[0].GetProperty("id").GetGuid();
        var secondOperationId = operations[1].GetProperty("id").GetGuid();

        (await admin.PostAsync($"/api/work-orders/{workOrderId}/start", null)).EnsureSuccessStatusCode();

        // 跳序报工 → 409
        var skipResponse = await admin.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{secondOperationId}/report",
            new { goodQuantity = 10, defectQuantity = 0, scrapQuantity = 0 });
        Assert.Equal(HttpStatusCode.Conflict, skipResponse.StatusCode);

        // 第一道工序报工 6 良品 → 仍进行中
        var firstReport = await admin.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{firstOperationId}/report",
            new { goodQuantity = 6, defectQuantity = 0, scrapQuantity = 0 });
        firstReport.EnsureSuccessStatusCode();
        var afterFirst = await firstReport.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, afterFirst.GetProperty("status").GetInt32()); // InProgress

        // 补足到 10 → 第一道完成
        await admin.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{firstOperationId}/report",
            new { goodQuantity = 4, defectQuantity = 0, scrapQuantity = 0 });

        // 第二道工序报工 9 良品 1 不良 → 全部工序完成，工单完成
        var secondReport = await admin.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{secondOperationId}/report",
            new { goodQuantity = 9, defectQuantity = 1, scrapQuantity = 0, defectCode = "D001" });
        secondReport.EnsureSuccessStatusCode();

        var final = await secondReport.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, final.GetProperty("status").GetInt32()); // Completed
        Assert.Equal(9, final.GetProperty("completedQuantity").GetInt32()); // 取最后一道工序良品数
        Assert.NotNull(final.GetProperty("completedAt").GetString());
    }

    [Fact]
    public async Task 工序报工_超出计划数量返回400()
    {
        var admin = await LoginAsync();
        var productId = await CreateProductAsync(admin);
        await CreateActiveRoutingAsync(admin, productId, ["印刷"], ["OP-E-"]);
        var workOrderId = await CreateWorkOrderAsync(admin, productId, plannedQuantity: 5);

        var released = await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null);
        var operationId = (await released.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("operations").EnumerateArray().First().GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/work-orders/{workOrderId}/start", null)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/report",
            new { goodQuantity = 6, defectQuantity = 0, scrapQuantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<Guid> CreateProductAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code = NewCode("P"),
            name = "工序测试产品",
            spec = (string?)null,
            unit = "PCS",
            remark = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>创建工艺路线并激活，使它成为该产品的生效版本。</summary>
    private async Task CreateActiveRoutingAsync(
        HttpClient admin,
        Guid productId,
        string[] stepNames,
        string[] operationPrefixes)
    {
        var steps = new List<object>();
        for (var index = 0; index < stepNames.Length; index++)
        {
            var operationResponse = await admin.PostAsJsonAsync("/api/master-data/operations", new
            {
                code = NewCode(operationPrefixes[index]),
                name = stepNames[index],
                standardSeconds = 60,
                isKeyOperation = false,
                defaultWorkCenterId = (Guid?)null,
                remark = (string?)null,
            });
            operationResponse.EnsureSuccessStatusCode();
            var operationId = (await operationResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetGuid();

            steps.Add(new
            {
                sequence = (index + 1) * 10,
                operationId,
                workCenterId = (Guid?)null,
                standardSeconds = 60,
                isQualityGate = false,
            });
        }

        var routingResponse = await admin.PostAsJsonAsync("/api/master-data/routings", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            steps,
        });
        routingResponse.EnsureSuccessStatusCode();

        var routingId = (await routingResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        (await admin.PostAsync($"/api/master-data/routings/{routingId}/activate", null)).EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateWorkOrderAsync(HttpClient admin, Guid productId, int plannedQuantity = 100)
    {
        var response = await admin.PostAsJsonAsync("/api/work-orders", new
        {
            productId,
            plannedQuantity,
            plannedStart = (DateTime?)null,
            plannedEnd = (DateTime?)null,
            workCenter = "LINE-01",
            remark = (string?)null,
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
