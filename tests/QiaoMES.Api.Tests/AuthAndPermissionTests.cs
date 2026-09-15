using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 端到端验证认证与权限：登录、令牌、按权限放行/拒绝。
/// </summary>
[Collection(ApiCollection.Name)]
public class AuthAndPermissionTests(QiaoMESApiFactory factory)
{
    private const string AdminUserName = "admin";
    private const string AdminPassword = "Admin123!";
    private const string OperatorPassword = "Operator123!";

    [Fact]
    public async Task 存活探针_无需认证且返回200()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 未登录访问工单列表_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/work-orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 密码错误_返回401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = AdminUserName, password = "definitely-wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 管理员登录后_可读取自身权限()
    {
        var admin = await LoginAsync(AdminUserName, AdminPassword);

        var me = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.Equal(AdminUserName, me.GetProperty("username").GetString());
        Assert.Contains("workorders:create", me.GetProperty("permissions")
            .EnumerateArray().Select(p => p.GetString()));
    }

    [Fact]
    public async Task 操作员创建工单_返回403()
    {
        var admin = await LoginAsync(AdminUserName, AdminPassword);
        var (operatorClient, _) = await CreateOperatorAsync(admin);

        var response = await operatorClient.PostAsJsonAsync("/api/work-orders", NewWorkOrderPayload(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 操作员访问用户管理_返回403()
    {
        var admin = await LoginAsync(AdminUserName, AdminPassword);
        var (operatorClient, _) = await CreateOperatorAsync(admin);

        var response = await operatorClient.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task 操作员查看工单列表_放行()
    {
        var admin = await LoginAsync(AdminUserName, AdminPassword);
        var (operatorClient, _) = await CreateOperatorAsync(admin);

        var response = await operatorClient.GetAsync("/api/work-orders?page=1&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 管理员创建工单_单号符合约定格式()
    {
        var admin = await LoginAsync(AdminUserName, AdminPassword);
        var productId = await CreateProductAsync(admin);

        var response = await admin.PostAsJsonAsync("/api/work-orders", NewWorkOrderPayload(productId));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderNumber = body.GetProperty("orderNumber").GetString();

        Assert.Matches(@"^WO-\d{8}-\d{4}$", orderNumber!);
    }

    [Fact]
    public async Task 并发创建工单_不产生重复单号()
    {
        var admin = await LoginAsync(AdminUserName, AdminPassword);
        var productId = await CreateProductAsync(admin);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => admin.PostAsJsonAsync("/api/work-orders", NewWorkOrderPayload(productId)))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        var orderNumbers = new List<string>();

        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            orderNumbers.Add(body.GetProperty("orderNumber").GetString()!);
        }

        Assert.Equal(orderNumbers.Count, orderNumbers.Distinct(StringComparer.Ordinal).Count());
    }

    private static object NewWorkOrderPayload(Guid productId) => new
    {
        productId,
        plannedQuantity = 10,
        plannedStart = (DateTime?)null,
        plannedEnd = (DateTime?)null,
        workCenter = "LINE-01",
        remark = (string?)null,
    };

    /// <summary>工单必须挂到真实产品上（创建时会校验产品存在且启用）。</summary>
    private static async Task<Guid> CreateProductAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/master-data/products", new
        {
            code = $"P{Guid.NewGuid():N}"[..12],
            name = "工单测试产品",
            spec = (string?)null,
            unit = "PCS",
            remark = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());

        return client;
    }

    /// <summary>用管理员身份建一个操作员账号，并返回已登录的客户端与其用户名。</summary>
    private async Task<(HttpClient Client, string Username)> CreateOperatorAsync(HttpClient admin)
    {
        var username = $"op_{Guid.NewGuid():N}"[..12];

        var roles = await admin.GetFromJsonAsync<JsonElement>("/api/roles");
        var operatorRoleId = roles.EnumerateArray()
            .First(role => role.GetProperty("name").GetString() == "operator")
            .GetProperty("id")
            .GetString();

        var response = await admin.PostAsJsonAsync("/api/users", new
        {
            username,
            password = OperatorPassword,
            displayName = "集成测试操作员",
            email = (string?)null,
            roleIds = new[] { operatorRoleId },
        });
        response.EnsureSuccessStatusCode();

        var client = await LoginAsync(username, OperatorPassword);
        return (client, username);
    }
}
