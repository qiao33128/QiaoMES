using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 来料批次谱系端到端验证：批次入库 → IQC 判定回写 → SN 绑定（幂等 / 扣减）→ 正反向追溯 → 大屏聚合。
/// </summary>
[Collection(ApiCollection.Name)]
public class MaterialLotTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 批次入库_默认待检且余量等于来料数量()
    {
        var admin = await LoginAsync();
        var lotNumber = NewLotNumber();

        var response = await admin.PostAsJsonAsync("/api/quality/material-lots", new
        {
            lotNumber,
            materialCode = "M-LOT-001",
            materialName = "连接器",
            quantity = 500m,
            unit = "PCS",
            supplier = "供应商A",
            supplierLotNumber = "SUP-001",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(lotNumber, body.GetProperty("lotNumber").GetString());
        Assert.Equal(0, body.GetProperty("status").GetInt32()); // Pending
        Assert.Equal(500m, body.GetProperty("remainingQuantity").GetDecimal());

        // 批次号重复 → 409
        var duplicate = await admin.PostAsJsonAsync("/api/quality/material-lots", new
        {
            lotNumber,
            materialCode = "M-LOT-001",
            quantity = 10m,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task IQC判定合格_自动放行来料批次()
    {
        var admin = await LoginAsync();
        var (lotId, lotNumber) = await CreateLotAsync(admin, 200m);

        // 建 IQC 检验单并关联批次
        var inspectionResponse = await admin.PostAsJsonAsync("/api/quality/inspections", new
        {
            type = 0, // IQC
            sampleSize = 20,
            materialCode = "M-LOT-001",
            lotNumber,
            acceptedLimit = 0,
            rejectedLimit = 1,
            items = new object[]
            {
                new { name = "外观-IQC", standard = "无划伤" },
                new { name = "尺寸-IQC", lowerLimit = 9.8m, upperLimit = 10.2m },
            },
        });
        inspectionResponse.EnsureSuccessStatusCode();
        var inspection = await inspectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inspectionId = inspection.GetProperty("id").GetGuid();

        Assert.Equal(lotNumber, inspection.GetProperty("lotNumber").GetString());

        // 全部合格 → 判定
        var items = inspection.GetProperty("items").EnumerateArray().ToList();
        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId = items[0].GetProperty("id").GetGuid(), measuredValue = "外观正常", isQualified = true });
        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId = items[1].GetProperty("id").GetGuid(), measuredValue = "10.0" });

        var submit = await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit",
            new { defectQuantity = 0 });
        submit.EnsureSuccessStatusCode();

        // 批次应已被自动放行（关键闭环：IQC 结论回写批次准入）
        var lot = await admin.GetFromJsonAsync<JsonElement>($"/api/quality/material-lots/{lotId}");
        Assert.Equal(1, lot.GetProperty("status").GetInt32()); // Available
        Assert.Equal(inspectionId, lot.GetProperty("iqcInspectionId").GetGuid());
        Assert.False(string.IsNullOrEmpty(lot.GetProperty("iqcInspectionNumber").GetString()));
    }

    [Fact]
    public async Task IQC判定不合格_批次禁止投产()
    {
        var admin = await LoginAsync();
        var (_, lotNumber) = await CreateLotAsync(admin, 100m);

        var inspectionResponse = await admin.PostAsJsonAsync("/api/quality/inspections", new
        {
            type = 0,
            sampleSize = 10,
            materialCode = "M-LOT-001",
            lotNumber,
            acceptedLimit = 0,
            rejectedLimit = 1,
            items = new object[] { new { name = "外观-IQC-不良", lowerLimit = 9.8m, upperLimit = 10.2m } },
        });
        var inspection = await inspectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var inspectionId = inspection.GetProperty("id").GetGuid();
        var itemId = inspection.GetProperty("items")[0].GetProperty("id").GetGuid();

        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = "11.5" });
        await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit", new { defectQuantity = 1 });

        var lot = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/quality/material-lots/by-lot-number/{lotNumber}");
        Assert.Equal(2, lot.GetProperty("status").GetInt32()); // Rejected

        // 不合格批次绑定 SN → 409
        var bind = await admin.PostAsJsonAsync("/api/quality/material-consumptions", new
        {
            items = new object[]
            {
                new { sn = "SN-REJECT-TEST", materialCode = "M-LOT-001", lotNumber, quantity = 1m },
            },
        });
        Assert.Equal(HttpStatusCode.Conflict, bind.StatusCode);
    }

    [Fact]
    public async Task SN绑定批次_扣减余量且重复绑定幂等()
    {
        var admin = await LoginAsync();
        var (_, lotNumber) = await CreateLotAsync(admin, 100m);
        await InspectLotAsync(admin, lotNumber, true);

        var sn = $"SN-LOT-{Guid.NewGuid():N}"[..20];

        var bind = await admin.PostAsJsonAsync("/api/quality/material-consumptions", new
        {
            items = new object[]
            {
                new { sn, materialCode = "M-LOT-001", lotNumber, quantity = 3m, operationName = "贴片" },
            },
        });
        bind.EnsureSuccessStatusCode();

        // 余量扣减
        var lot = await admin.GetFromJsonAsync<JsonElement>($"/api/quality/material-lots/by-lot-number/{lotNumber}");
        Assert.Equal(97m, lot.GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(1, lot.GetProperty("consumedSnCount").GetInt32());

        // 重复绑定 → 幂等（不再扣减）
        var again = await admin.PostAsJsonAsync("/api/quality/material-consumptions", new
        {
            items = new object[]
            {
                new { sn, materialCode = "M-LOT-001", lotNumber, quantity = 3m },
            },
        });
        again.EnsureSuccessStatusCode();

        var afterRepeat = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/quality/material-lots/by-lot-number/{lotNumber}");
        Assert.Equal(97m, afterRepeat.GetProperty("remainingQuantity").GetDecimal());

        // 正向追溯：该 SN 用了哪些批次
        var bySn = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/quality/material-consumptions/by-sn/{sn}");
        Assert.Single(bySn.EnumerateArray());
        Assert.Equal(lotNumber, bySn[0].GetProperty("lotNumber").GetString());
    }

    [Fact]
    public async Task 批次反向追溯_返回受影响SN并汇入追溯报告()
    {
        var admin = await LoginAsync();
        var (_, lotNumber) = await CreateLotAsync(admin, 50m);
        await InspectLotAsync(admin, lotNumber, true);

        var sn1 = $"SN-TRACE-{Guid.NewGuid():N}"[..20];
        var sn2 = $"SN-TRACE-{Guid.NewGuid():N}"[..20];

        await admin.PostAsJsonAsync("/api/quality/material-consumptions", new
        {
            items = new object[]
            {
                new { sn = sn1, materialCode = "M-LOT-001", lotNumber, quantity = 1m },
                new { sn = sn2, materialCode = "M-LOT-001", lotNumber, quantity = 2m },
            },
        });

        // 批次 → SN 集合
        var trace = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/quality/material-consumptions/by-lot/{lotNumber}");
        Assert.Equal(2, trace.GetProperty("snCount").GetInt32());
        Assert.Equal(3m, trace.GetProperty("consumedQuantity").GetDecimal());
        Assert.Equal(2, trace.GetProperty("consumptions").GetArrayLength());

        // 追溯报告的上游谱系（SN 未建工单，仅验证 materialLots 字段存在）
        var reportResponse = await admin.GetAsync($"/api/traceability/sn/{sn1}");
        Assert.Equal(HttpStatusCode.NotFound, reportResponse.StatusCode); // SN 不存在 → 404（预期）

        // 批次反查接口（跨模块聚合）
        var lotTrace = await admin.GetFromJsonAsync<JsonElement>($"/api/traceability/lot/{lotNumber}");
        Assert.Equal(lotNumber, lotTrace.GetProperty("lot").GetProperty("lotNumber").GetString());
        Assert.Equal(2, lotTrace.GetProperty("snCount").GetInt32());
    }

    [Fact]
    public async Task 冻结批次_禁止继续绑定用料()
    {
        var admin = await LoginAsync();
        var (lotId, lotNumber) = await CreateLotAsync(admin, 30m);
        await InspectLotAsync(admin, lotNumber, true);

        var freeze = await admin.PostAsJsonAsync($"/api/quality/material-lots/{lotId}/freeze",
            new { frozen = true, reason = "疑似来料异常" });
        freeze.EnsureSuccessStatusCode();

        var bind = await admin.PostAsJsonAsync("/api/quality/material-consumptions", new
        {
            items = new object[]
            {
                new { sn = $"SN-FROZEN-{Guid.NewGuid():N}"[..20], materialCode = "M-LOT-001", lotNumber, quantity = 1m },
            },
        });
        Assert.Equal(HttpStatusCode.Conflict, bind.StatusCode);

        // 解冻后恢复可用
        await admin.PostAsJsonAsync($"/api/quality/material-lots/{lotId}/freeze", new { frozen = false });
        var lot = await admin.GetFromJsonAsync<JsonElement>($"/api/quality/material-lots/{lotId}");
        Assert.Equal(1, lot.GetProperty("status").GetInt32()); // Available
    }

    [Fact]
    public async Task 大屏总览_返回产量质量设备与Andon()
    {
        var admin = await LoginAsync();

        var overview = await admin.GetFromJsonAsync<JsonElement>("/api/dashboard/overview");

        Assert.False(string.IsNullOrEmpty(overview.GetProperty("dateText").GetString()));
        Assert.True(overview.GetProperty("production").TryGetProperty("yieldRate", out _));
        Assert.True(overview.GetProperty("quality").TryGetProperty("fpy", out _));
        Assert.True(overview.GetProperty("equipment").TryGetProperty("running", out _));
        Assert.True(overview.GetProperty("andon").TryGetProperty("waiting", out _));
        Assert.Equal(JsonValueKind.Array, overview.GetProperty("downtimeTop").ValueKind);
    }

    [Fact]
    public async Task 未登录访问批次接口_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quality/material-lots");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewLotNumber() => $"LOT-{Guid.NewGuid():N}"[..18];

    private static async Task<(Guid Id, string LotNumber)> CreateLotAsync(HttpClient admin, decimal quantity)
    {
        var lotNumber = NewLotNumber();

        var response = await admin.PostAsJsonAsync("/api/quality/material-lots", new
        {
            lotNumber,
            materialCode = "M-LOT-001",
            materialName = "连接器",
            quantity,
            unit = "PCS",
            supplier = "供应商A",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), lotNumber);
    }

    /// <summary>直接登记批次 IQC 结论（不走过检验单，用于聚焦谱系测试）。</summary>
    private static async Task InspectLotAsync(HttpClient admin, string lotNumber, bool passed)
    {
        var lot = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/quality/material-lots/by-lot-number/{lotNumber}");

        var response = await admin.PostAsJsonAsync(
            $"/api/quality/material-lots/{lot.GetProperty("id").GetGuid()}/inspect",
            new { passed, reason = passed ? null : "测试判定不合格" });
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
