using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QiaoMES.Infrastructure.Outbox;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 阶段 4 · 对外集成验证：开放 API 独立鉴权（X-Api-Key）、密钥发放 / 停用、设备采集上报经 Outbox 异步应用。
/// </summary>
[Collection(ApiCollection.Name)]
public class OpenApiTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";
    private const string ProbeOrderNumber = "OPENAPI-PROBE-NOT-EXIST";

    [Fact]
    public async Task 开放API_无密钥返回401_有效密钥可访问()
    {
        var admin = await LoginAsync();

        // 未携带密钥
        var anonymous = factory.CreateClient();
        var unauthorized = await anonymous.GetAsync($"/api/open/v1/work-orders/{ProbeOrderNumber}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        // 无效密钥
        var bogus = factory.CreateClient();
        bogus.DefaultRequestHeaders.Add("X-Api-Key", "qmk_invalid_key_value");
        var invalid = await bogus.GetAsync($"/api/open/v1/work-orders/{ProbeOrderNumber}");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        // 有效密钥：鉴权通过（工单不存在 → 404，而不是 401）
        var (erp, _) = await CreateClientAsync(admin, "ERP 集成测试");
        var response = await erp.GetAsync($"/api/open/v1/work-orders/{ProbeOrderNumber}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 停用密钥_立即失效()
    {
        var admin = await LoginAsync();
        var (erp, clientId) = await CreateClientAsync(admin, "待停用客户端");

        Assert.Equal(HttpStatusCode.NotFound,
            (await erp.GetAsync($"/api/open/v1/work-orders/{ProbeOrderNumber}")).StatusCode);

        var disable = await admin.PutAsync($"/api/integration/api-clients/{clientId}/active?isActive=false", null);
        disable.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await erp.GetAsync($"/api/open/v1/work-orders/{ProbeOrderNumber}")).StatusCode);
    }

    [Fact]
    public async Task 密钥只返回一次_列表仅暴露前缀()
    {
        var admin = await LoginAsync();
        var (_, clientId) = await CreateClientAsync(admin, "前缀校验客户端");

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/integration/api-clients?page=1&pageSize=50");
        var item = list.GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("id").GetGuid() == clientId);

        Assert.False(item.TryGetProperty("plainKey", out _));
        Assert.Contains("…", item.GetProperty("keyPreview").GetString());
        Assert.True(item.GetProperty("isUsable").GetBoolean());
    }

    [Fact]
    public async Task 设备采集上报_落Outbox并异步应用()
    {
        var admin = await LoginAsync();
        var (erp, _) = await CreateClientAsync(admin, "设备网关测试");
        var equipmentCode = $"EQ-OPEN-{Guid.NewGuid():N}"[..18];

        var report = await erp.PostAsJsonAsync("/api/open/v1/equipment-telemetry", new
        {
            equipmentCode,
            status = 2,
            reasonCode = "E-STOP",
            reason = "急停触发",
        });

        // 采集端立即返回 202，不阻塞在状态机处理上
        Assert.Equal(HttpStatusCode.Accepted, report.StatusCode);

        // 事件与业务（此处为接入动作）同一事务落库
        using (var scope = factory.Services.CreateScope())
        {
            var outboxDb = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var message = await outboxDb.OutboxMessages
                .FirstOrDefaultAsync(m => m.Payload.Contains(equipmentCode));

            Assert.NotNull(message);
            Assert.Contains("EquipmentTelemetryReceivedEvent", message!.EventType);
        }

        await ProcessPendingAsync();

        // 设备未登记 → 处理器记录告警并结束，事件标记成功（不会无限重试）
        using (var scope = factory.Services.CreateScope())
        {
            var outboxDb = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            var message = await outboxDb.OutboxMessages
                .FirstAsync(m => m.Payload.Contains(equipmentCode));

            Assert.Equal(OutboxStatus.Succeeded, message.Status);
        }
    }

    // ---------------- 辅助方法 ----------------

    private async Task<(HttpClient Client, Guid ClientId)> CreateClientAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/integration/api-clients", new { name });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var plainKey = body.GetProperty("plainKey").GetString()!;
        var clientId = body.GetProperty("client").GetProperty("id").GetGuid();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", plainKey);

        return (client, clientId);
    }

    private async Task ProcessPendingAsync()
    {
        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();

        for (var round = 0; round < 3; round++)
        {
            await processor.ProcessPendingAsync(200);
        }
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
