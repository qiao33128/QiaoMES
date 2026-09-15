using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// SN 生成、过站（进站/出站）与追溯轨迹的端到端验证。
/// </summary>
[Collection(ApiCollection.Name)]
public class SerialNumberTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task SN过站全流程_不合格停留_合格后流转直至完工()
    {
        var admin = await LoginAsync();
        var (workOrderId, orderNumber, operationIds) = await CreateReleasedWorkOrderAsync(admin);

        // 生成 2 颗 SN
        var generateResponse = await admin.PostAsJsonAsync("/api/production/serial-numbers/generate",
            new { workOrderId, quantity = 2 });
        generateResponse.EnsureSuccessStatusCode();
        var generated = await generateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sns = generated.EnumerateArray().Select(s => s.GetProperty("sn").GetString()!).ToList();

        Assert.Equal(2, sns.Count);
        Assert.Equal($"{orderNumber}-0001", sns[0]);
        Assert.Equal($"{orderNumber}-0002", sns[1]);

        var sn = sns[0];

        // 进站（第一道工序）
        (await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-in",
            new { operationTaskId = operationIds[0] })).EnsureSuccessStatusCode();

        // 重复进站 → 409
        var duplicate = await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-in",
            new { operationTaskId = operationIds[0] });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // 出站不合格 → 仍停留在第一道工序
        var failResponse = await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-out",
            new { operationTaskId = operationIds[0], result = 2 });
        failResponse.EnsureSuccessStatusCode();
        var afterFail = await failResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(operationIds[0], afterFail.GetProperty("serialNumber").GetProperty("currentOperationTaskId").GetGuid());

        // 出站合格 → 离开第一道工序
        var passResponse = await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-out",
            new { operationTaskId = operationIds[0], result = 1 });
        passResponse.EnsureSuccessStatusCode();
        var afterPass = await passResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, afterPass.GetProperty("serialNumber").GetProperty("currentOperationTaskId").ValueKind);

        // 第二道工序：进站 + 出站合格（最后一道）→ 整颗完工
        (await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-in",
            new { operationTaskId = operationIds[1] })).EnsureSuccessStatusCode();
        var finishResponse = await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-out",
            new { operationTaskId = operationIds[1], result = 1 });
        finishResponse.EnsureSuccessStatusCode();

        var finished = await finishResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, finished.GetProperty("serialNumber").GetProperty("status").GetInt32()); // Completed
        Assert.NotNull(finished.GetProperty("serialNumber").GetProperty("completedAt").GetString());

        // 轨迹：2 次进站 + 3 次出站（含 1 次不合格）
        var trackings = finished.GetProperty("trackings").EnumerateArray().ToList();
        Assert.Equal(5, trackings.Count);
        Assert.Contains(trackings, t => t.GetProperty("result").GetInt32() == 2);
    }

    [Fact]
    public async Task SN未进站时不能出站_返回409()
    {
        var admin = await LoginAsync();
        var (workOrderId, _, operationIds) = await CreateReleasedWorkOrderAsync(admin);

        var generateResponse = await admin.PostAsJsonAsync("/api/production/serial-numbers/generate",
            new { workOrderId, quantity = 1 });
        var sn = (await generateResponse.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("sn").GetString();

        var response = await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-out",
            new { operationTaskId = operationIds[0], result = 1 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task 按SN查询_可追溯当前工序与历史轨迹()
    {
        var admin = await LoginAsync();
        var (workOrderId, orderNumber, operationIds) = await CreateReleasedWorkOrderAsync(admin);

        var generateResponse = await admin.PostAsJsonAsync("/api/production/serial-numbers/generate",
            new { workOrderId, quantity = 1 });
        var sn = (await generateResponse.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("sn").GetString();

        (await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-in",
            new { operationTaskId = operationIds[0] })).EnsureSuccessStatusCode();

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/production/serial-numbers/{sn}");

        Assert.Equal(sn, detail.GetProperty("serialNumber").GetProperty("sn").GetString());
        Assert.Equal(orderNumber, detail.GetProperty("serialNumber").GetProperty("orderNumber").GetString());
        Assert.Equal(1, detail.GetProperty("trackings").GetArrayLength());
    }

    [Fact]
    public async Task 不存在的SN_返回404()
    {
        var admin = await LoginAsync();

        var response = await admin.GetAsync("/api/production/serial-numbers/SN-NOT-EXIST-9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    /// <summary>建产品 → 建两步工艺路线并激活 → 建工单 → 下达 → 开始生产。</summary>
    private async Task<(Guid WorkOrderId, string OrderNumber, List<Guid> OperationIds)> CreateReleasedWorkOrderAsync(
        HttpClient admin)
    {
        var productResponse = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code = NewCode("P"),
            name = "SN 测试产品",
            spec = (string?)null,
            unit = "PCS",
            remark = (string?)null,
        });
        productResponse.EnsureSuccessStatusCode();
        var productId = (await productResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var steps = new List<object>();
        var operationIds = new List<Guid>();

        for (var index = 0; index < 2; index++)
        {
            var operationResponse = await admin.PostAsJsonAsync("/api/master-data/operations", new
            {
                code = NewCode($"OP{index}-"),
                name = index == 0 ? "印刷" : "贴片",
                standardSeconds = 60,
                isKeyOperation = false,
                defaultWorkCenterId = (Guid?)null,
                remark = (string?)null,
            });
            operationResponse.EnsureSuccessStatusCode();

            var operationId = (await operationResponse.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("id").GetGuid();
            operationIds.Add(operationId);

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
        var routingId = (await routingResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/master-data/routings/{routingId}/activate", null)).EnsureSuccessStatusCode();

        var orderResponse = await admin.PostAsJsonAsync("/api/work-orders", new
        {
            productId,
            plannedQuantity = 10,
            plannedStart = (DateTime?)null,
            plannedEnd = (DateTime?)null,
            workCenter = "LINE-01",
            remark = (string?)null,
        });
        orderResponse.EnsureSuccessStatusCode();
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = order.GetProperty("id").GetGuid();
        var orderNumber = order.GetProperty("orderNumber").GetString()!;

        var releaseResponse = await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null);
        releaseResponse.EnsureSuccessStatusCode();
        var released = await releaseResponse.Content.ReadFromJsonAsync<JsonElement>();
        var taskIds = released.GetProperty("operations").EnumerateArray()
            .Select(o => o.GetProperty("id").GetGuid())
            .ToList();

        (await admin.PostAsync($"/api/work-orders/{workOrderId}/start", null)).EnsureSuccessStatusCode();

        return (workOrderId, orderNumber, taskIds);
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
