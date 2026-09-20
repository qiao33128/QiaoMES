using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QiaoMES.Assistant.Api;
using QiaoMES.Assistant.Infrastructure;
using QiaoMES.Identity.Api;
using QiaoMES.Identity.Application;
using QiaoMES.Identity.Infrastructure;
using QiaoMES.Identity.Infrastructure.Persistence;
using QiaoMES.Identity.Infrastructure.Security;
using QiaoMES.Infrastructure;
using QiaoMES.Infrastructure.Logging;
using QiaoMES.Infrastructure.UnitOfWork;
using QiaoMES.MasterData.Api;
using QiaoMES.MasterData.Infrastructure;
using QiaoMES.MasterData.Infrastructure.Persistence;
using QiaoMES.Quality.Api;
using QiaoMES.Quality.Infrastructure;
using QiaoMES.Equipment.Api;
using QiaoMES.Equipment.Api.Hubs;
using QiaoMES.Equipment.Infrastructure;
using QiaoMES.Equipment.Infrastructure.Persistence;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using QiaoMES.Api.EventHandlers;
using QiaoMES.Infrastructure.Integrations;
using QiaoMES.Infrastructure.Outbox;
using QiaoMES.Reporting.Api;
using QiaoMES.Reporting.Infrastructure;
using QiaoMES.Reporting.Infrastructure.Persistence;
using QiaoMES.Shared.IntegrationEvents;
using QiaoMES.Quality.Infrastructure.Persistence;
using QiaoMES.Production.Api;
using QiaoMES.Production.Api.Hubs;
using QiaoMES.Production.Infrastructure;
using QiaoMES.Production.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// ---------- 日志（生产环境输出 JSON，便于集中采集） ----------
if (builder.Environment.IsProduction())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });
}

// ---------- 横切基础设施（共享连接、事务、异常处理、权限授权、健康检查） ----------
builder.Services.AddQiaoMESInfrastructure(builder.Configuration);

// ---------- 模块注册 ----------
builder.Services.AddIdentityModule();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddProductionModule();
builder.Services.AddProductionInfrastructure();
builder.Services.AddMasterDataModule();
builder.Services.AddMasterDataInfrastructure();
builder.Services.AddQualityModule();
builder.Services.AddQualityInfrastructure();
builder.Services.AddEquipmentModule();
builder.Services.AddEquipmentInfrastructure();
builder.Services.AddReportingModule();
builder.Services.AddReportingInfrastructure();
builder.Services.AddAssistantModule();
builder.Services.AddAssistantInfrastructure(builder.Configuration);

// ---------- 改进建议与迭代审阅（转发给 AI 迭代服务，权限闸门留在宿主） ----------
builder.Services.Configure<QiaoMES.Api.Iteration.IterationOptions>(
    builder.Configuration.GetSection(QiaoMES.Api.Iteration.IterationOptions.SectionName));

// 客户端直接注入强类型选项（省掉到处 IOptions<T>.Value）。配置在启动时定型：
// 改地址 / 密钥需要重启，这与宿主其它外部集成的行为一致。
builder.Services.AddSingleton(provider =>
    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<QiaoMES.Api.Iteration.IterationOptions>>().Value);
builder.Services.AddHttpClient<QiaoMES.Api.Iteration.IterationClient>((provider, client) =>
{
    var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<QiaoMES.Api.Iteration.IterationOptions>>().Value;

    if (options.IsConfigured)
    {
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    }

    // 超时由客户端内部按配置的 TimeoutSeconds 统一控制，避免两层超时打架
    client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
});

// ---------- 演示数据生成器（仅 Development 环境可用，见 DemoDataController） ----------
builder.Services.AddScoped<QiaoMES.Api.Seed.DemoDataSeeder>();

// ---------- 模块间集成事件订阅（Outbox 异步投递）----------
// 检验不合格 → 自动发起 Andon 呼叫（设备模块）；质量模块无需反向依赖设备模块
builder.Services.AddIntegrationEvent<InspectionJudgedEvent, InspectionJudgedEventHandler>();
// 设备采集上报（开放 API）→ 设备模块应用状态机
builder.Services.AddIntegrationEvent<EquipmentTelemetryReceivedEvent, EquipmentTelemetryEventHandler>();

// ---------- 对外集成（开放 API：独立鉴权 + 按密钥限流）----------
builder.Services.AddIntegrations();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(IntegrationExtensions.OpenApiRateLimitPolicy, context =>
    {
        // 有密钥按密钥分片（每个外部系统一份配额），无密钥退化为按来源 IP
        var apiKey = context.Request.Headers[ApiKeyAuthenticationDefaults.HeaderName].ToString();
        var partitionKey = string.IsNullOrWhiteSpace(apiKey)
            ? $"ip:{context.Connection.RemoteIpAddress}"
            : $"key:{ApiClient.ComputeHash(apiKey)[..16]}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

// ---------- 让工作单元收集到全部模块 DbContext ----------
// EF 的 AddDbContext<T> 只注册具体类型，不会注册 DbContext 基类；
// 不补这一步，UnitOfWorkFilter 的 GetServices<DbContext>() 会拿到空集合，跨模块事务形同虚设。
QiaoMES.Infrastructure.UnitOfWork.UnitOfWorkExtensions.RegisterDbContexts(builder.Services);

// ---------- 控制器注册（集中配置 + 全局工作单元过滤器） ----------
builder.Services.AddControllers()
    .AddUnitOfWork()
    .AddIdentityControllers()
    .AddProductionControllers()
    .AddMasterDataControllers()
    .AddQualityControllers()
    .AddEquipmentControllers()
    .AddReportingControllers()
    .AddAssistantControllers();

// ---------- 认证授权（JWT + 权限策略） ----------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // 允许 SignalR 通过 query string 传递 token
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    })
    // 开放 API 独立鉴权：X-Api-Key（与 JWT 并存，可各自吊销、各自限流）
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.Scheme, null);
builder.Services.AddAuthorization();

// ---------- 通用基础设施 ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration["Cors:Origins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
            ?? ["http://localhost:5173"];
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var securityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "请输入 JWT 令牌",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference
        {
            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
            Id = "Bearer",
        },
    };
    options.AddSecurityDefinition("Bearer", securityScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

var app = builder.Build();

// ---------- 中间件管道 ----------
// 异常处理必须在最外层，才能兜住后续所有中间件与业务代码抛出的异常
app.UseExceptionHandler();
app.UseRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<ProductionHub>("/hubs/production");
// Andon 实时看板通道
app.MapHub<AndonHub>("/hubs/andon");

// ---------- 健康检查 ----------
// live：进程存活（不查依赖）；ready：依赖就绪（查数据库连通性）
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// ---------- 数据库迁移与种子数据 ----------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var identityDb = services.GetRequiredService<IdentityDbContext>();
    var productionDb = services.GetRequiredService<ProductionDbContext>();
    var masterDataDb = services.GetRequiredService<MasterDataDbContext>();
    var qualityDb = services.GetRequiredService<QualityDbContext>();
    var equipmentDb = services.GetRequiredService<EquipmentDbContext>();
    var reportingDb = services.GetRequiredService<ReportingDbContext>();
    var outboxDb = services.GetRequiredService<OutboxDbContext>();
    var integrationDb = services.GetRequiredService<IntegrationDbContext>();
    var assistantDb = services.GetRequiredService<QiaoMES.Assistant.Infrastructure.Persistence.AssistantDbContext>();
    await identityDb.Database.MigrateAsync();
    await productionDb.Database.MigrateAsync();
    await masterDataDb.Database.MigrateAsync();
    await qualityDb.Database.MigrateAsync();
    await equipmentDb.Database.MigrateAsync();
    await reportingDb.Database.MigrateAsync();
    await outboxDb.Database.MigrateAsync();
    await integrationDb.Database.MigrateAsync();
    await assistantDb.Database.MigrateAsync();

    // 智能问数：把库里保存的模型配置应用到运行时（没有配置行就沿用 appsettings / 环境变量）。
    // 必须在迁移之后 —— 否则首次部署时表还不存在。
    await services.GetRequiredService<QiaoMES.Assistant.Application.IAssistantSettingsStore>()
        .ApplyPersistedAsync();

    var passwordHasher = services.GetRequiredService<IPasswordHasher>();
    await IdentityDbSeeder.SeedAsync(identityDb, passwordHasher);
}

app.Run();

/// <summary>供集成测试通过 <c>WebApplicationFactory&lt;Program&gt;</c> 引用。</summary>
public partial class Program;
