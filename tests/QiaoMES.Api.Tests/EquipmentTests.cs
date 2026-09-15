using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 设备模块端到端验证：台账、状态机（含故障原因强制）、状态汇总、点检保养、Andon 呼叫闭环。
/// </summary>
[Collection(ApiCollection.Name)]
public class EquipmentTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 创建设备_默认待机状态且编号唯一()
    {
        var admin = await LoginAsync();
        var code = NewCode("EQ");

        var response = await admin.PostAsJsonAsync("/api/equipment/equipments", new
        {
            code,
            name = "贴片机 #1",
            model = "YS12",
            serialNumber = "SN-EQ-001",
            lineName = "SMT-1",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("status").GetInt32()); // Idle
        Assert.True(body.GetProperty("isActive").GetBoolean());

        // 编号重复 → 409
        var duplicate = await admin.PostAsJsonAsync("/api/equipment/equipments", new { code, name = "重复设备" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task 状态切换_故障必须填原因且记录停机时长()
    {
        var admin = await LoginAsync();
        var equipmentId = await CreateEquipmentAsync(admin);

        // 转入运行
        var running = await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status",
            new { status = 0 });
        running.EnsureSuccessStatusCode();
        Assert.Equal(0, (await running.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32());

        // 故障不带原因 → 400
        var invalid = await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status",
            new { status = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // 故障带原因 → 成功
        var down = await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status",
            new { status = 2, reasonCode = "BREAKDOWN", reason = "吸嘴堵塞" });
        down.EnsureSuccessStatusCode();
        var downBody = await down.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, downBody.GetProperty("status").GetInt32());
        Assert.Equal("BREAKDOWN", downBody.GetProperty("downReasonCode").GetString());

        // 恢复运行 → 生成状态轨迹
        var recover = await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status",
            new { status = 0, reason = "已清理吸嘴" });
        recover.EnsureSuccessStatusCode();
        var recovered = await recover.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, recovered.GetProperty("status").GetInt32());
        Assert.Equal(3, recovered.GetProperty("statusLogs").GetArrayLength());
    }

    [Fact]
    public async Task 设备状态汇总_返回各状态数量()
    {
        var admin = await LoginAsync();
        var equipmentId = await CreateEquipmentAsync(admin);
        await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status", new { status = 0 });

        var summary = await admin.GetFromJsonAsync<JsonElement>("/api/equipment/equipments/summary");

        Assert.True(summary.GetProperty("total").GetInt32() >= 1);
        Assert.True(summary.GetProperty("running").GetInt32() >= 1);
    }

    [Fact]
    public async Task 登记点检记录_正常与异常都可留痕()
    {
        var admin = await LoginAsync();
        var equipmentId = await CreateEquipmentAsync(admin);

        var normal = await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/maintenance", new
        {
            type = 0, // DailyCheck
            content = "气压、导轨润滑检查",
            result = 0,
        });
        normal.EnsureSuccessStatusCode();

        var abnormal = await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/maintenance", new
        {
            type = 1, // Maintenance
            content = "月度保养",
            result = 1,
            abnormalDescription = "皮带磨损需更换",
        });
        abnormal.EnsureSuccessStatusCode();

        var body = await abnormal.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("maintenanceRecords").GetArrayLength());
        Assert.Contains(
            body.GetProperty("maintenanceRecords").EnumerateArray(),
            record => record.GetProperty("result").GetInt32() == 1
                      && record.GetProperty("abnormalDescription").GetString() == "皮带磨损需更换");
    }

    [Fact]
    public async Task Andon呼叫闭环_呼叫_响应_解决()
    {
        var admin = await LoginAsync();
        var equipmentId = await CreateEquipmentAsync(admin);

        // 一键呼叫（带设备，自动带出设备编码）
        var callResponse = await admin.PostAsJsonAsync("/api/equipment/andon-calls", new
        {
            type = 0, // EquipmentFailure
            description = "贴片机报警 E-204，产线停线",
            level = 1, // Red
            equipmentId,
            timeoutMinutes = 5,
        });
        callResponse.EnsureSuccessStatusCode();

        var call = await callResponse.Content.ReadFromJsonAsync<JsonElement>();
        var callId = call.GetProperty("id").GetGuid();

        Assert.StartsWith("ANDON-", call.GetProperty("callNumber").GetString());
        Assert.Equal(0, call.GetProperty("status").GetInt32()); // Waiting
        Assert.False(call.GetProperty("isTimeout").GetBoolean());
        Assert.False(string.IsNullOrEmpty(call.GetProperty("equipmentCode").GetString()));

        // 响应
        var respond = await admin.PostAsync($"/api/equipment/andon-calls/{callId}/respond", null);
        respond.EnsureSuccessStatusCode();
        var responded = await respond.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, responded.GetProperty("status").GetInt32());
        Assert.NotNull(responded.GetProperty("respondedAt").GetString());

        // 重复响应 → 409
        var duplicate = await admin.PostAsync($"/api/equipment/andon-calls/{callId}/respond", null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // 解决
        var resolve = await admin.PostAsJsonAsync($"/api/equipment/andon-calls/{callId}/resolve",
            new { resolution = "更换吸嘴后恢复正常" });
        resolve.EnsureSuccessStatusCode();
        var resolved = await resolve.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, resolved.GetProperty("status").GetInt32());

        // 未结束呼叫过滤
        var open = await admin.GetFromJsonAsync<JsonElement>("/api/equipment/andon-calls?onlyOpen=true&page=1&pageSize=50");
        Assert.DoesNotContain(
            open.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == callId);
    }

    [Fact]
    public async Task 停机Pareto_按原因代码聚合()
    {
        var admin = await LoginAsync();
        var equipmentId = await CreateEquipmentAsync(admin);
        var reasonCode = $"BRK-{Guid.NewGuid():N}"[..10];

        await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status",
            new { status = 2, reasonCode, reason = "测试停机" });
        await admin.PostAsJsonAsync($"/api/equipment/equipments/{equipmentId}/status",
            new { status = 0, reason = "恢复" });

        var pareto = await admin.GetFromJsonAsync<JsonElement>("/api/equipment/equipments/downtime-pareto?top=20");

        Assert.Contains(
            pareto.EnumerateArray(),
            item => item.GetProperty("reasonCode").GetString() == reasonCode);
    }

    [Fact]
    public async Task 未登录访问设备接口_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/equipment/equipments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

    private static async Task<Guid> CreateEquipmentAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/equipment/equipments", new
        {
            code = NewCode("EQ"),
            name = "测试设备",
            model = "TEST-1",
            lineName = "SMT-TEST",
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
