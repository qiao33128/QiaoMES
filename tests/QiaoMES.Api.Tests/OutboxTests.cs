using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Infrastructure.Outbox;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 阶段 4 · 模块间事件化（Outbox）验证：<para>
/// 业务动作 → 事件与业务同一事务落库 → 分发器异步投递 → 订阅方（设备模块）产生副作用；
/// 并验证「至少投递一次」下的幂等保护。
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public class OutboxTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";

    [Fact]
    public async Task 检验不合格_事件落库并投递为Andon红灯呼叫()
    {
        var admin = await LoginAsync();
        var sn = NewSn();

        // 1. 建 FQC 检验单并判定不合格
        var (inspectionId, inspectionNumber, itemId) = await CreateInspectionAsync(admin, sn, InspectionTypeFqc);

        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = "11.5", defectCode = "D-SIZE" });

        var submit = await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit",
            new { defectQuantity = 2 });
        submit.EnsureSuccessStatusCode();

        // 2. 事件已在 Outbox 落库（与业务数据同一事务）
        using (var scope = factory.Services.CreateScope())
        {
            var outboxDb = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var total = await outboxDb.OutboxMessages.CountAsync();
            Assert.True(total > 0, "Outbox 表为空：集成事件未与业务数据一起落库");

            var message = await outboxDb.OutboxMessages
                .FirstOrDefaultAsync(m => m.Payload.Contains(inspectionNumber));

            Assert.NotNull(message);
            Assert.Contains(nameof(InspectionJudgedEvent), message!.EventType);
        }

        // 3. 手工驱动一轮分发（不依赖后台时序，保证断言确定性）
        await ProcessPendingAsync();

        // 4. 设备模块出现「质量异常 + 红灯」的 Andon 呼叫
        var matched = await FindAndonCallsAsync(admin, inspectionNumber);
        Assert.Single(matched);

        // 5. 幂等：再跑一轮分发，不会重复建呼叫（Outbox 至少投递一次）
        await ProcessPendingAsync();

        var matchedAgain = await FindAndonCallsAsync(admin, inspectionNumber);
        Assert.Single(matchedAgain);
    }

    [Fact]
    public async Task 检验合格_事件落库但订阅方不产生呼叫()
    {
        var admin = await LoginAsync();
        var sn = NewSn();

        var (inspectionId, inspectionNumber, itemId) = await CreateInspectionAsync(admin, sn, InspectionTypeFqc);

        await admin.PutAsJsonAsync($"/api/quality/inspections/{inspectionId}/items/record",
            new { itemId, measuredValue = "10.0" });

        var submit = await admin.PostAsJsonAsync($"/api/quality/inspections/{inspectionId}/submit",
            new { defectQuantity = 0 });
        submit.EnsureSuccessStatusCode();

        using (var scope = factory.Services.CreateScope())
        {
            var outboxDb = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var message = await outboxDb.OutboxMessages
                .FirstOrDefaultAsync(m => m.Payload.Contains(inspectionNumber));
            Assert.NotNull(message);
        }

        await ProcessPendingAsync();

        // 合格不建 Andon 呼叫
        var matched = await FindAndonCallsAsync(admin, inspectionNumber);
        Assert.Empty(matched);
    }

    // ---------------- 辅助方法 ----------------

    private const int InspectionTypeFqc = 2;

    private static string NewSn() => $"SN-OBX-{Guid.NewGuid():N}"[..18];

    private static async Task<(Guid Id, string Number, Guid ItemId)> CreateInspectionAsync(
        HttpClient admin,
        string sn,
        int type)
    {
        var response = await admin.PostAsJsonAsync("/api/quality/inspections", new
        {
            type,
            sampleSize = 5,
            sn,
            productCode = "P-OUTBOX-1",
            acceptedLimit = 0,
            rejectedLimit = 1,
            items = new object[]
            {
                new { name = $"外观-{Guid.NewGuid():N}"[..12], lowerLimit = 9.8m, upperLimit = 10.2m },
            },
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            body.GetProperty("id").GetGuid(),
            body.GetProperty("inspectionNumber").GetString()!,
            body.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private async Task ProcessPendingAsync()
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();

        // 连续跑几轮，确保重试中的消息也被推进
        for (var round = 0; round < 3; round++)
        {
            await processor.ProcessPendingAsync(100);
        }
    }

    private static async Task<List<JsonElement>> FindAndonCallsAsync(HttpClient admin, string inspectionNumber)
    {
        var andon = await admin.GetFromJsonAsync<JsonElement>(
            "/api/equipment/andon-calls?onlyOpen=true&page=1&pageSize=100");

        return andon.GetProperty("items").EnumerateArray()
            .Where(call => (call.GetProperty("description").GetString() ?? string.Empty)
                .Contains(inspectionNumber, StringComparison.Ordinal))
            .ToList();
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
