using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace QiaoMES.Infrastructure.Logging;

/// <summary>
/// 请求日志中间件：统一记录 TraceId、用户、路径、状态码与耗时。
/// <para>TraceId 同时写入响应头 <c>X-Trace-Id</c>，便于前端报障时直接提供。</para>
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private const string TraceIdHeader = "X-Trace-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        // 健康检查探针不写日志，避免噪音
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var traceId = context.TraceIdentifier;
        context.Response.Headers[TraceIdHeader] = traceId;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var level = statusCode >= StatusCodes.Status500InternalServerError
                ? LogLevel.Error
                : statusCode >= StatusCodes.Status400BadRequest
                    ? LogLevel.Warning
                    : LogLevel.Information;

            logger.Log(level,
                "HTTP {Method} {Path} → {StatusCode} {ElapsedMs}ms TraceId={TraceId} User={User}",
                context.Request.Method,
                context.Request.Path.Value,
                statusCode,
                stopwatch.ElapsedMilliseconds,
                traceId,
                context.User.Identity?.Name ?? "anonymous");
        }
    }
}

public static class RequestLoggingExtensions
{
    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
        => app.UseMiddleware<RequestLoggingMiddleware>();
}
