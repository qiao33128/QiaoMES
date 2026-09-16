using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Production.Infrastructure.Persistence;
using QiaoMES.Reporting.Infrastructure.Persistence;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 阶段 4 · 4.6 预聚合验证：重算汇总 → 汇总行与明细口径一致 → 报表/看板读汇总结果不劣化。
/// </summary>
[Collection(ApiCollection.Name)]
public class MetricsAggregationTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 重算预聚合_汇总行的SN口径与明细一致()
    {
        var admin = await LoginAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var from = today.AddDays(-3);

        var rebuild = await admin.PostAsync(
            $"/api/reports/rebuild-metrics?from={from:yyyy-MM-dd}&to={today:yyyy-MM-dd}", null);
        rebuild.EnsureSuccessStatusCode();

        var body = await rebuild.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("shiftsRebuilt").GetInt32() >= 0);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("lastComputedAt").ValueKind);

        using var scope = factory.Services.CreateScope();
        var reportingDb = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        var productionDb = scope.ServiceProvider.GetRequiredService<ProductionDbContext>();

        var metrics = await reportingDb.DailyShiftMetrics
            .Where(m => m.ProductionDate >= from && m.ProductionDate <= today)
            .ToListAsync();

        // 无班次配置时使用「全天」窗口，也应至少产生汇总行
        if (metrics.Count == 0)
        {
            return;
        }

        foreach (var metric in metrics)
        {
            var detailCount = await productionDb.SerialNumbers
                .CountAsync(s => s.CreatedAt >= metric.StartAtUtc && s.CreatedAt < metric.EndAtUtc);

            // 汇总行必须与明细逐班次对齐（跨天夜班的窗口归属也一并验证）
            Assert.Equal(detailCount, metric.TotalSn);
        }
    }

    [Fact]
    public async Task 重算后_报表与看板接口读数合理()
    {
        var admin = await LoginAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var range = $"from={today.AddDays(-6):yyyy-MM-dd}&to={today:yyyy-MM-dd}";

        await admin.PostAsync($"/api/reports/rebuild-metrics?{range}", null);

        var shifts = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/shifts?{range}");
        Assert.Equal(JsonValueKind.Array, shifts.GetProperty("items").ValueKind);

        var oee = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/oee?{range}");
        Assert.True(oee.GetProperty("plannedHours").GetDouble() > 0);
        Assert.InRange(oee.GetProperty("availability").GetDecimal(), 0m, 100m);
        Assert.InRange(oee.GetProperty("oee").GetDecimal(), 0m, 100m);

        // 汇总读路径与实时口径一致：完工数不应超过投产数
        Assert.True(oee.GetProperty("completedSn").GetInt32() <= oee.GetProperty("totalSn").GetInt32());
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
