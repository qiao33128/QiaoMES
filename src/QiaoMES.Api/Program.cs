using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
using QiaoMES.Reporting.Api;
using QiaoMES.Reporting.Infrastructure;
using QiaoMES.Reporting.Infrastructure.Persistence;
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

// ---------- 控制器注册（集中配置 + 全局工作单元过滤器） ----------
builder.Services.AddControllers()
    .AddUnitOfWork()
    .AddIdentityControllers()
    .AddProductionControllers()
    .AddMasterDataControllers()
    .AddQualityControllers()
    .AddEquipmentControllers()
    .AddReportingControllers();

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
    });
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
    await identityDb.Database.MigrateAsync();
    await productionDb.Database.MigrateAsync();
    await masterDataDb.Database.MigrateAsync();
    await qualityDb.Database.MigrateAsync();
    await equipmentDb.Database.MigrateAsync();
    await reportingDb.Database.MigrateAsync();

    var passwordHasher = services.GetRequiredService<IPasswordHasher>();
    await IdentityDbSeeder.SeedAsync(identityDb, passwordHasher);
}

app.Run();

/// <summary>供集成测试通过 <c>WebApplicationFactory&lt;Program&gt;</c> 引用。</summary>
public partial class Program;
