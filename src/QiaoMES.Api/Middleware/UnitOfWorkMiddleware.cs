using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace QiaoMES.Api.Middleware;

/// <summary>
/// 工作单元过滤器：在 Action 成功执行后统一保存所有 DbContext 的待持久化变更。
/// 避免在每个应用服务中手动调用 SaveChangesAsync。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class UnitOfWorkFilter : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        // 仅在 Action 成功执行且未返回错误时保存
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            return;
        }
        if (executed.Result is ObjectResult { StatusCode: >= 400 })
        {
            return;
        }
        if (executed.Result is StatusCodeResult { StatusCode: >= 400 })
        {
            return;
        }

        var dbContexts = context.HttpContext.RequestServices
            .GetServices<DbContext>()
            .Where(db => db.ChangeTracker.HasChanges())
            .ToList();

        foreach (var db in dbContexts)
        {
            await db.SaveChangesAsync();
        }
    }
}

public static class UnitOfWorkExtensions
{
    /// <summary>全局启用工作单元过滤器。</summary>
    public static IMvcBuilder AddUnitOfWork(this IMvcBuilder builder)
    {
        builder.Services.Configure<MvcOptions>(options =>
            options.Filters.Add<UnitOfWorkFilter>());
        return builder;
    }
}
