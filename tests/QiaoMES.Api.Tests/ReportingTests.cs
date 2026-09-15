using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 阶段 4 指标与班次验证：班次（含跨天夜班）、生产日历、班次口径换算、OEE / 达成率 / 质量 / 停机报表、CSV 导出。
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportingTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 班次定义_跨天夜班可识别()
    {
        var admin = await LoginAsync();
        var code = NewCode("DAY");

        var response = await admin.PostAsJsonAsync("/api/reporting/shifts", new
        {
            code,
            name = "白班",
            startTime = "08:30:00",
            endTime = "20:30:00",
            sequence = 1,
        });
        response.EnsureSuccessStatusCode();

        var day = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(day.GetProperty("crossesMidnight").GetBoolean());

        var nightCode = NewCode("NGT");
        var nightResponse = await admin.PostAsJsonAsync("/api/reporting/shifts", new
        {
            code = nightCode,
            name = "夜班",
            startTime = "20:30:00",
            endTime = "08:30:00",
            sequence = 2,
        });
        nightResponse.EnsureSuccessStatusCode();

        var night = await nightResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(night.GetProperty("crossesMidnight").GetBoolean());

        // 重复代码 → 409
        var duplicate = await admin.PostAsJsonAsync("/api/reporting/shifts", new
        {
            code,
            name = "重复",
            startTime = "09:00:00",
            endTime = "18:00:00",
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task 当前班次_返回生产日与时间窗()
    {
        var admin = await LoginAsync();
        await EnsureShiftAsync(admin, NewCode("CUR"), "当前班次", "00:00:00", "23:59:59");

        var current = await admin.GetFromJsonAsync<JsonElement>("/api/reporting/shifts/current");

        Assert.False(string.IsNullOrEmpty(current.GetProperty("shiftCode").GetString()));
        Assert.False(string.IsNullOrEmpty(current.GetProperty("productionDate").GetString()));
        Assert.True(current.GetProperty("durationHours").GetDouble() > 0);
    }

    [Fact]
    public async Task 班次窗口换算_跨天夜班归属于开始日()
    {
        var admin = await LoginAsync();

        var dayCode = NewCode("SD");
        var nightCode = NewCode("SN");
        await EnsureShiftAsync(admin, dayCode, "报表白班", "08:00:00", "20:00:00", lineName: "LINE-RPT", sequence: 1);
        await EnsureShiftAsync(admin, nightCode, "报表夜班", "20:00:00", "08:00:00", lineName: "LINE-RPT", sequence: 2);

        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-2);
        var ranges = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reporting/shifts/ranges?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}&lineName=LINE-RPT");

        Assert.True(ranges.GetArrayLength() >= 4, $"班次窗口数量异常：{ranges.GetArrayLength()}");

        // 夜班窗口：开始当天 20:00，结束次日 08:00（本地）
        var night = ranges.EnumerateArray()
            .First(r => r.GetProperty("shiftCode").GetString() == nightCode);
        var startLocal = night.GetProperty("startAtUtc").GetDateTime().ToLocalTime();
        var endLocal = night.GetProperty("endAtUtc").GetDateTime().ToLocalTime();

        Assert.Equal(20, startLocal.Hour);
        Assert.Equal(8, endLocal.Hour);
        Assert.Equal(startLocal.Date.AddDays(1), endLocal.Date);
        Assert.Equal(startLocal.Date, night.GetProperty("productionDate").GetDateTime().Date);
    }

    [Fact]
    public async Task 日历_节假日不计入计划生产时间()
    {
        var admin = await LoginAsync();
        var holiday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        var lineName = $"LINE-CAL-{Guid.NewGuid():N}"[..16];

        await EnsureShiftAsync(admin, NewCode("CAL"), "日历班次", "08:00:00", "20:00:00", lineName: lineName);

        // 标记为节假日
        var upsert = await admin.PostAsJsonAsync("/api/reporting/calendar", new
        {
            date = holiday.ToString("yyyy-MM-dd"),
            isWorkingDay = false,
            name = "测试节假日",
        });
        upsert.EnsureSuccessStatusCode();

        var ranges = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reporting/shifts/ranges?from={holiday:yyyy-MM-dd}&to={holiday:yyyy-MM-dd}&lineName={lineName}");

        Assert.Equal(0, ranges.GetArrayLength());

        // 调休改回工作日
        await admin.PostAsJsonAsync("/api/reporting/calendar", new
        {
            date = holiday.ToString("yyyy-MM-dd"),
            isWorkingDay = true,
            name = "调休上班",
        });

        var after = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reporting/shifts/ranges?from={holiday:yyyy-MM-dd}&to={holiday:yyyy-MM-dd}&lineName={lineName}");
        Assert.Single(after.EnumerateArray());
    }

    [Fact]
    public async Task OEE报表_返回可用率性能良率三要素()
    {
        var admin = await LoginAsync();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var oee = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/reports/oee?from={today.AddDays(-6):yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.True(oee.GetProperty("plannedHours").GetDouble() > 0);
        Assert.InRange(oee.GetProperty("availability").GetDecimal(), 0m, 100m);
        Assert.InRange(oee.GetProperty("performance").GetDecimal(), 0m, 100m);
        Assert.InRange(oee.GetProperty("quality").GetDecimal(), 0m, 100m);
        Assert.InRange(oee.GetProperty("oee").GetDecimal(), 0m, 100m);
        Assert.True(oee.GetProperty("runHours").GetDouble() <= oee.GetProperty("plannedHours").GetDouble());
    }

    [Fact]
    public async Task 质量与达成率与停机报表_返回结构完整()
    {
        var admin = await LoginAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var range = $"from={today.AddDays(-30):yyyy-MM-dd}&to={today:yyyy-MM-dd}";

        var quality = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/quality?{range}");
        Assert.True(quality.GetProperty("inspectionTotal").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Array, quality.GetProperty("topDefects").ValueKind);

        var achievement = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/achievement?{range}");
        Assert.True(achievement.GetProperty("orderCount").GetInt32() >= 0);
        Assert.InRange(achievement.GetProperty("achievementRate").GetDecimal(), 0m, 10000m);

        var downtime = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/downtime?{range}");
        Assert.True(downtime.GetProperty("totalDownSeconds").GetInt64() >= 0);
        Assert.Equal(JsonValueKind.Array, downtime.GetProperty("byReason").ValueKind);

        var shiftMetrics = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/shifts?{range}");
        Assert.Equal(JsonValueKind.Array, shiftMetrics.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task CSV导出_带BOM且列头正确()
    {
        var admin = await LoginAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);

        var response = await admin.GetAsync(
            $"/api/reports/export?type=shift&from={today.AddDays(-3):yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        response.EnsureSuccessStatusCode();
        Assert.Contains("text/csv", response.Content.Headers.ContentType?.ToString() ?? string.Empty);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("生产日", text);
        Assert.Contains("一次合格率FPY%", text);

        // UTF-8 BOM（Excel 中文不乱码）
        Assert.True(
            bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            $"缺少 UTF-8 BOM，前 3 字节：{bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2}");
    }

    [Fact]
    public async Task 未登录访问报表_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reports/oee");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------------- 辅助方法 ----------------

    private static string NewCode(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..10];

    private static async Task EnsureShiftAsync(
        HttpClient admin,
        string code,
        string name,
        string startTime,
        string endTime,
        string? lineName = null,
        int sequence = 0)
    {
        var response = await admin.PostAsJsonAsync("/api/reporting/shifts", new
        {
            code,
            name,
            startTime,
            endTime,
            lineName,
            sequence,
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
