using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 集成测试宿主：启动真实的 HTTP 管道（含中间件、认证、授权、事务过滤器）。
/// <para>
/// 需要本地 PostgreSQL（默认 <c>localhost:5432</c>，库 <c>qiaomes</c>）。
/// 可在 CI 中通过 GitHub Actions 的 postgres service 提供。
/// </para>
/// </summary>
public sealed class QiaoMESApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        return base.CreateHost(builder);
    }
}

[CollectionDefinition(ApiCollection.Name)]
public sealed class ApiCollection : ICollectionFixture<QiaoMESApiFactory>
{
    public const string Name = "qiaomes-api";
}
