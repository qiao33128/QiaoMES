using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 追溯端到端验证：从一张真实工单走完「建产品 → BOM → 工艺路线 → 下达 → SN 过站 → 检验不合格 → 处置」，
/// 再拉取「人机料法环」追溯报告与批次影响范围。
/// </summary>
[Collection(ApiCollection.Name)]
public class TraceabilityTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 追溯报告_包含人机料法环完整信息()
    {
        var admin = await LoginAsync();

        // ---------- 1. 主数据：产品 / 物料 / 工序 ----------
        var productId = await CreateProductAsync(admin);
        var materialId = await CreateMaterialAsync(admin);
        var operationIds = await CreateOperationsAsync(admin);

        // 2. BOM 并激活
        var bomResponse = await admin.PostAsJsonAsync("/api/master-data/boms", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            items = new object[]
            {
                new { materialId, quantity = 2m, unit = "PCS", lossRate = 0.01m, remark = (string?)null },
            },
        });
        bomResponse.EnsureSuccessStatusCode();
        var bomId = (await bomResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/master-data/boms/{bomId}/activate", null)).EnsureSuccessStatusCode();

        // 3. 工艺路线并激活
        var routingResponse = await admin.PostAsJsonAsync("/api/master-data/routings", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            steps = operationIds.Select((operationId, index) => new
            {
                sequence = (index + 1) * 10,
                operationId,
                workCenterId = (Guid?)null,
                standardSeconds = 60,
                isQualityGate = false,
            }).ToArray(),
        });
        routingResponse.EnsureSuccessStatusCode();
        var routingId = (await routingResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/master-data/routings/{routingId}/activate", null)).EnsureSuccessStatusCode();

        // 4. 工单 → 下达（展开工序）
        var orderResponse = await admin.PostAsJsonAsync("/api/work-orders", new
        {
            productId,
            plannedQuantity = 10,
            plannedStart = (DateTime?)null,
            plannedEnd = (DateTime?)null,
            workCenter = "LINE-TRACE",
            remark = (string?)null,
        });
        orderResponse.EnsureSuccessStatusCode();
        var order = await orderResponse.Content.ReadFromJsonAsync<JsonElement>();
        var workOrderId = order.GetProperty("id").GetGuid();

        var releaseResponse = await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null);
        releaseResponse.EnsureSuccessStatusCode();
        var released = await releaseResponse.Content.ReadFromJsonAsync<JsonElement>();
        var taskIds = released.GetProperty("operations").EnumerateArray()
            .Select(o => o.GetProperty("id").GetGuid())
            .ToList();

        // 5. 生成 SN 并走完第一道工序
        var generateResponse = await admin.PostAsJsonAsync("/api/production/serial-numbers/generate",
            new { workOrderId, quantity = 1 });
        var sn = (await generateResponse.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("sn").GetString()!;

        (await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-in",
            new { operationTaskId = taskIds[0], equipmentId = (Guid?)null })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/production/serial-numbers/{sn}/track-out",
            new { operationTaskId = taskIds[0], result = 1 })).EnsureSuccessStatusCode();

        // 6. 检验不合格 → 自动派生处置单
        var inspectionResponse = await admin.PostAsJsonAsync("/api/quality/inspections", new
        {
            type = 2, // FQC
            sampleSize = 5,
            sn,
            productCode = "P-TRACE",
            acceptedLimit = 0,
            rejectedLimit = 1,
            items = new object[]
            {
                new { name = "长度-追溯", lowerLimit = 9.8m, upperLimit = 10.2m },
            },
        });
        inspectionResponse.EnsureSuccessStatusCode();
        var inspection = await inspectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inspectionId = inspection.GetProperty("id").GetGuid();
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();

        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = "10.8", defectCode = "D-TRACE" });
        await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit", new
        {
            defectQuantity = 1,
            createNonconformance = true,
            defectDescription = "长度超差",
        });

        // ---------- 7. 拉取追溯报告 ----------
        var report = await admin.GetFromJsonAsync<JsonElement>($"/api/traceability/sn/{sn}");

        Assert.Equal(sn, report.GetProperty("sn").GetString());

        // 人 + 机：过站轨迹（进站 + 出站）
        var trackings = report.GetProperty("serialNumber").GetProperty("trackings");
        Assert.Equal(2, trackings.GetArrayLength());
        Assert.Contains(trackings.EnumerateArray(), t => t.GetProperty("action").GetInt32() == 0);

        // 法：工艺路线（下达时快照）
        Assert.Equal("V1.0", report.GetProperty("routing").GetProperty("version").GetString());
        Assert.Equal(2, report.GetProperty("routing").GetProperty("steps").GetArrayLength());

        // 料：BOM 明细
        Assert.Equal("V1.0", report.GetProperty("bom").GetProperty("version").GetString());
        Assert.Equal(1, report.GetProperty("bom").GetProperty("items").GetArrayLength());

        // 工单快照版本号
        Assert.Equal("V1.0", report.GetProperty("workOrder").GetProperty("routingVersion").GetString());
        Assert.Equal("V1.0", report.GetProperty("workOrder").GetProperty("bomVersion").GetString());

        // 环：检验记录（不合格）+ 处置单
        var inspectionsInReport = report.GetProperty("inspections");
        Assert.True(inspectionsInReport.GetArrayLength() >= 1);
        Assert.Contains(inspectionsInReport.EnumerateArray(), i => i.GetProperty("status").GetInt32() == 3);

        var ncrs = report.GetProperty("nonconformances");
        Assert.True(ncrs.GetArrayLength() >= 1);
        Assert.Contains(ncrs.EnumerateArray(), n => n.GetProperty("sn").GetString() == sn);
    }

    [Fact]
    public async Task 批次影响范围_汇总同工单SN与不良数量()
    {
        var admin = await LoginAsync();

        var productId = await CreateProductAsync(admin);
        var operationIds = await CreateOperationsAsync(admin);
        await ActivateRoutingAsync(admin, productId, operationIds);

        var orderResponse = await admin.PostAsJsonAsync("/api/work-orders", new
        {
            productId,
            plannedQuantity = 5,
            plannedStart = (DateTime?)null,
            plannedEnd = (DateTime?)null,
            workCenter = "LINE-BATCH",
            remark = (string?)null,
        });
        var workOrderId = (await orderResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/work-orders/{workOrderId}/release", null)).EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync("/api/production/serial-numbers/generate",
            new { workOrderId, quantity = 3 })).EnsureSuccessStatusCode();

        var batch = await admin.GetFromJsonAsync<JsonElement>($"/api/traceability/batch/{workOrderId}");

        Assert.Equal(workOrderId, batch.GetProperty("workOrderId").GetGuid());
        Assert.Equal(3, batch.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, batch.GetProperty("serialNumbers").GetArrayLength());
        Assert.Equal(0, batch.GetProperty("completedCount").GetInt32());
    }

    [Fact]
    public async Task 不存在的SN_追溯返回404()
    {
        var admin = await LoginAsync();

        var response = await admin.GetAsync("/api/traceability/sn/SN-NOT-EXIST-TRACE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private static async Task<Guid> CreateProductAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code = NewCode("P"),
            name = "追溯测试产品",
            unit = "PCS",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateMaterialAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/materials", new
        {
            code = NewCode("M"),
            name = "追溯测试物料",
            materialType = 0,
            unit = "PCS",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> CreateOperationsAsync(HttpClient admin)
    {
        var operationIds = new List<Guid>();

        foreach (var name in new[] { "印刷", "贴片" })
        {
            var response = await admin.PostAsJsonAsync("/api/master-data/operations", new
            {
                code = NewCode("OP"),
                name,
                standardSeconds = 60,
                isKeyOperation = false,
            });
            response.EnsureSuccessStatusCode();
            operationIds.Add((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        }

        return operationIds;
    }

    private static async Task ActivateRoutingAsync(HttpClient admin, Guid productId, List<Guid> operationIds)
    {
        var routingResponse = await admin.PostAsJsonAsync("/api/master-data/routings", new
        {
            productId,
            version = "V1.0",
            remark = (string?)null,
            steps = operationIds.Select((operationId, index) => new
            {
                sequence = (index + 1) * 10,
                operationId,
                workCenterId = (Guid?)null,
                standardSeconds = 60,
                isQualityGate = false,
            }).ToArray(),
        });
        routingResponse.EnsureSuccessStatusCode();

        var routingId = (await routingResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PostAsync($"/api/master-data/routings/{routingId}/activate", null)).EnsureSuccessStatusCode();
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
