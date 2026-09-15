using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QiaoMES.Infrastructure.Exceptions;

/// <summary>
/// 全局异常处理器：任何未捕获异常都转换为标准 <c>application/problem+json</c>。
/// <para>生产环境不返回堆栈与异常详情，只返回 <c>traceId</c>（可用它去日志里定位）。</para>
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            // 响应已开始写出，无法再改写响应体，交给服务器记录
            logger.LogError(exception, "响应已开始写出，异常无法转换为 ProblemDetails");
            return false;
        }

        var traceId = httpContext.TraceIdentifier;
        var (statusCode, title) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "请求内容格式不正确"),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested
                => (StatusCodes.Status499ClientClosedRequest, "请求已被客户端取消"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "没有访问该资源的权限"),
            _ => (StatusCodes.Status500InternalServerError, "服务器内部错误"),
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "未处理异常 TraceId={TraceId} Method={Method} Path={Path}",
                traceId, httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "请求异常 TraceId={TraceId} Method={Method} Path={Path}",
                traceId, httpContext.Request.Method, httpContext.Request.Path);
        }

        var problem = new ProblemDetails
        {
            Title = title,
            Status = statusCode,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = traceId;

        // 仅开发环境暴露异常详情，避免生产环境泄露内部实现
        if (environment.IsDevelopment())
        {
            problem.Extensions["exception"] = exception.GetType().Name;
            problem.Extensions["detail"] = exception.Message;
            problem.Extensions["stackTrace"] = exception.StackTrace;
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }
}
