using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QiaoMES.Api.Middleware;
using QiaoMES.Identity.Api;
using QiaoMES.Identity.Infrastructure;
using QiaoMES.Identity.Infrastructure.Security;
using QiaoMES.Production.Api;
using QiaoMES.Production.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---------- 模块注册 ----------
builder.Services.AddIdentityModule();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddProductionModule();
builder.Services.AddProductionInfrastructure(builder.Configuration);

// ---------- 控制器注册（集中配置 + 全局工作单元过滤器） ----------
builder.Services.AddControllers()
    .AddUnitOfWork()
    .AddIdentityControllers()
    .AddProductionControllers();

// ---------- 认证授权（JWT） ----------
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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<QiaoMES.Production.Api.Hubs.ProductionHub>("/hubs/production");

// ---------- 数据库迁移与种子数据 ----------
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var identityDb = services.GetRequiredService<QiaoMES.Identity.Infrastructure.Persistence.IdentityDbContext>();
    var productionDb = services.GetRequiredService<QiaoMES.Production.Infrastructure.Persistence.ProductionDbContext>();
    await identityDb.Database.MigrateAsync();
    await productionDb.Database.MigrateAsync();

    var passwordHasher = services.GetRequiredService<QiaoMES.Identity.Application.IPasswordHasher>();
    await QiaoMES.Identity.Infrastructure.Persistence.IdentityDbSeeder.SeedAsync(identityDb, passwordHasher);
}

app.Run();
