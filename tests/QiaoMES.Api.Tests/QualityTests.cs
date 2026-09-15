using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 质量模块端到端验证：检验单（IQC/IPQC/FQC 共用）、定量自动判定、
/// 不合格自动派生处置单、维修 → 复检闭环、不良代码与 SPC 判异。
/// </summary>
[Collection(ApiCollection.Name)]
public class QualityTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 创建检验单_单号按类型与日期编号且带检验项()
    {
        var admin = await LoginAsync();

        var response = await admin.PostAsJsonAsync("/api/quality/inspections", new
        {
            type = 0, // IQC
            sampleSize = 32,
            materialCode = "M-QA-001",
            aqlLevel = "AQL 1.0",
            acceptedLimit = 1,
            rejectedLimit = 2,
            items = new object[]
            {
                new { name = "外观", standard = "无划伤、无脏污" },
                new { name = "长度", lowerLimit = 9.8m, upperLimit = 10.2m },
            },
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var number = body.GetProperty("inspectionNumber").GetString();

        Assert.StartsWith("IQC-", number);
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        Assert.False(body.GetProperty("isFullyRecorded").GetBoolean());
    }

    [Fact]
    public async Task 定量检验项_按规格上下限自动判定合格与超差()
    {
        var admin = await LoginAsync();
        var inspection = await CreateInspectionAsync(admin, "长度-SPC-自动判定");
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();

        // 10.05 在 9.8 ~ 10.2 之间 → 自动判合格
        var qualified = await admin.PutAsJsonAsync(
            $"/api/quality/inspections/{inspection.GetProperty("id").GetGuid()}/items/record",
            new { itemId, measuredValue = "10.05" });
        qualified.EnsureSuccessStatusCode();
        var qualifiedBody = await qualified.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(qualifiedBody.GetProperty("items")[0].GetProperty("isQualified").GetBoolean());

        // 10.50 超上限 → 自动判不合格
        var failed = await admin.PutAsJsonAsync(
            $"/api/quality/inspections/{inspection.GetProperty("id").GetGuid()}/items/record",
            new { itemId, measuredValue = "10.50" });
        var failedBody = await failed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(failedBody.GetProperty("items")[0].GetProperty("isQualified").GetBoolean());
    }

    [Fact]
    public async Task 存在不合格项_判定失败并自动生成处置单()
    {
        var admin = await LoginAsync();
        var inspection = await CreateInspectionAsync(admin, "长度-SPC-不合格");
        var inspectionId = inspection.GetProperty("id").GetGuid();
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();

        await admin.PutAsJsonAsync(
            $"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = "10.9", defectCode = "D-SIZE" });

        var submit = await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit", new
        {
            defectQuantity = 1,
            createNonconformance = true,
            defectDescription = "长度超上限",
        });
        submit.EnsureSuccessStatusCode();

        var submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, submitted.GetProperty("status").GetInt32()); // Failed

        // 自动派生的处置单
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/quality/nonconformances?page=1&pageSize=5");
        var latest = list.GetProperty("items").EnumerateArray().First();

        Assert.StartsWith("NC-", latest.GetProperty("nonconformanceNumber").GetString());
        Assert.Equal(0, latest.GetProperty("status").GetInt32()); // Pending
        Assert.Equal("长度超上限", latest.GetProperty("defectDescription").GetString());
    }

    [Fact]
    public async Task 处置闭环_返修完成复检合格后关闭()
    {
        var admin = await LoginAsync();

        var createResponse = await admin.PostAsJsonAsync("/api/quality/nonconformances", new
        {
            quantity = 2,
            sn = $"SN-QA-{Guid.NewGuid():N}"[..16],
            defectCode = "D-WELD",
            defectDescription = "焊点虚焊",
        });
        createResponse.EnsureSuccessStatusCode();
        var ncr = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var ncrId = ncr.GetProperty("id").GetGuid();

        // 决定返修（需复检）
        var decide = await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/decide", new
        {
            disposition = 1, // Repair
            needReinspect = true,
        });
        decide.EnsureSuccessStatusCode();
        var decided = await decide.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, decided.GetProperty("status").GetInt32()); // InProgress

        // 登记维修
        var startRepair = await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/repairs", new
        {
            description = "重新补焊并清洗",
        });
        startRepair.EnsureSuccessStatusCode();
        var repairing = await startRepair.Content.ReadFromJsonAsync<JsonElement>();
        var repairId = repairing.GetProperty("repairs")[0].GetProperty("id").GetGuid();

        // 完成维修 → 待复检
        var complete = await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/repairs/complete", new
        {
            repairId,
            result = "补焊完成，外观正常",
        });
        complete.EnsureSuccessStatusCode();
        var completed = await complete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, completed.GetProperty("status").GetInt32()); // PendingReinspect

        // 复检合格 → 关闭
        var reinspect = await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/reinspect", new
        {
            passed = true,
            remark = "复检合格",
        });
        reinspect.EnsureSuccessStatusCode();

        var closed = await reinspect.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, closed.GetProperty("status").GetInt32()); // Closed
        Assert.NotNull(closed.GetProperty("closedAt").GetString());
    }

    [Fact]
    public async Task 复检不合格_退回处理中可再次维修()
    {
        var admin = await LoginAsync();

        var createResponse = await admin.PostAsJsonAsync("/api/quality/nonconformances", new
        {
            quantity = 1,
            defectCode = "D-RETEST",
        });
        var ncrId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/decide",
            new { disposition = 0, needReinspect = true })).EnsureSuccessStatusCode();

        var startRepair = await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/repairs",
            new { description = "返工尝试" });
        var repairId = (await startRepair.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("repairs")[0].GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/repairs/complete",
            new { repairId, result = "已处理" })).EnsureSuccessStatusCode();

        var reinspect = await admin.PostAsJsonAsync($"/api/quality/nonconformances/{ncrId}/reinspect",
            new { passed = false, remark = "复检仍不合格" });
        reinspect.EnsureSuccessStatusCode();

        var body = await reinspect.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("status").GetInt32()); // 回到 InProgress

        // 兜底报废
        var scrap = await admin.PostAsync($"/api/quality/nonconformances/{ncrId}/scrap?remark=无法修复", null);
        scrap.EnsureSuccessStatusCode();
        var scrapped = await scrap.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, scrapped.GetProperty("status").GetInt32());
        Assert.Equal(3, scrapped.GetProperty("disposition").GetInt32()); // Scrap
    }

    [Fact]
    public async Task 不良代码_创建后可用于Pareto统计()
    {
        var admin = await LoginAsync();
        var code = $"D{Guid.NewGuid():N}"[..10];

        var createResponse = await admin.PostAsJsonAsync("/api/quality/defect-codes", new
        {
            code,
            name = "划伤",
            category = "外观",
            description = "表面划伤",
        });
        createResponse.EnsureSuccessStatusCode();

        // 重复代码 → 409
        var duplicate = await admin.PostAsJsonAsync("/api/quality/defect-codes", new { code, name = "重复" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // 制造一次带该不良代码的检验 → 应出现在 Pareto 中
        var inspection = await CreateInspectionAsync(admin, $"长度-{code}");
        var inspectionId = inspection.GetProperty("id").GetGuid();
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();

        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = "11.0", defectCode = code });
        await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit",
            new { defectQuantity = 1 });

        var pareto = await admin.GetFromJsonAsync<JsonElement>("/api/quality/defect-codes/pareto?top=20");
        Assert.Contains(
            pareto.EnumerateArray(),
            item => item.GetProperty("defectCode").GetString() == code);
    }

    [Fact]
    public async Task SPC趋势_连续同侧时给出告警()
    {
        var admin = await LoginAsync();
        var itemName = $"长度-{Guid.NewGuid():N}"[..14];

        // 8 个低值 + 1 个高值 → 前 8 点位于均值同侧
        for (var index = 0; index < 8; index++)
        {
            await RecordSampleAsync(admin, itemName, "1.0");
        }
        await RecordSampleAsync(admin, itemName, "5.0");

        var trend = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/quality/spc/trend?itemName={Uri.EscapeDataString(itemName)}&points=20");

        Assert.Equal(9, trend.GetProperty("sampleCount").GetInt32());
        Assert.True(trend.GetProperty("hasSignal").GetBoolean());
        Assert.NotNull(trend.GetProperty("signalDescription").GetString());
        // 控制限是数值型字段
        Assert.NotEqual(JsonValueKind.Null, trend.GetProperty("upperControlLimit").ValueKind);
    }

    [Fact]
    public async Task 未登录访问质量接口_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quality/inspections");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    /// <summary>建一张带「长度」定量检验项的检验单（9.8 ~ 10.2）。</summary>
    private static async Task<JsonElement> CreateInspectionAsync(HttpClient admin, string itemName)
    {
        var response = await admin.PostAsJsonAsync("/api/quality/inspections", new
        {
            type = 1, // IPQC
            sampleSize = 10,
            sn = $"SN-{Guid.NewGuid():N}"[..16],
            productCode = "P-QA",
            acceptedLimit = 0,
            rejectedLimit = 1,
            items = new object[]
            {
                new { name = itemName, standard = "10 ± 0.2", lowerLimit = 9.8m, upperLimit = 10.2m },
            },
        });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>建单 → 录入数值 → 提交（用于积累 SPC 数据点）。</summary>
    private static async Task RecordSampleAsync(HttpClient admin, string itemName, string value)
    {
        var inspection = await CreateInspectionAsync(admin, itemName);
        var inspectionId = inspection.GetProperty("id").GetGuid();
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();

        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = value });
        await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit",
            new { defectQuantity = 0 });
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
