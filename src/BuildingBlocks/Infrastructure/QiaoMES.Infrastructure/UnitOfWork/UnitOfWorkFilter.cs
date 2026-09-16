using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QiaoMES.Shared;

namespace QiaoMES.Infrastructure.UnitOfWork;

/// <summary>
/// 工作单元过滤器：为每个 Action 开启**一个**数据库事务，把请求内所有 <see cref="DbContext"/> 的变更
/// 绑到同一事务上，成功则统一提交、失败则统一回滚。
/// <para>
/// 前提：各模块的 DbContext 共享同一个 <see cref="DbConnection"/>（见 <c>AddQiaoMESInfrastructure</c>），
/// 因此各模块必须位于同一物理数据库中。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class UnitOfWorkFilter(ILogger<UnitOfWorkFilter> logger) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var cancellationToken = context.HttpContext.RequestAborted;

        var connection = services.GetService<DbConnection>();
        var dbContexts = services.GetServices<DbContext>().ToList();

        // 未启用共享连接或没有 DbContext 时退化为「无事务」执行
        if (connection is null || dbContexts.Count == 0)
        {
            await next();
            return;
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var dbContext in dbContexts)
        {
            await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);
        }

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }

        if (HasFailed(executed))
        {
            await RollbackAsync(transaction);
            logger.LogDebug("请求失败，事务已回滚 Path={Path}", context.HttpContext.Request.Path);
            return;
        }

        try
        {
            foreach (var dbContext in dbContexts.Where(db => db.ChangeTracker.HasChanges()))
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }

        // 事务提交成功后才执行实时通知等副作用
        var postCommit = services.GetService<IPostCommitActions>();
        if (postCommit is not null)
        {
            await postCommit.ExecuteAsync(cancellationToken);
        }
    }

    private static bool HasFailed(ActionExecutedContext executed)
    {
        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            return true;
        }

        return executed.Result switch
        {
            ObjectResult { StatusCode: >= 400 } => true,
            StatusCodeResult { StatusCode: >= 400 } => true,
            JsonResult { StatusCode: >= 400 } => true,
            _ => false,
        };
    }

    private async Task RollbackAsync(DbTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "回滚事务失败");
        }
    }
}

public static class UnitOfWorkExtensions
{
    /// <summary>全局启用工作单元过滤器。</summary>
    public static IMvcBuilder AddUnitOfWork(this IMvcBuilder builder)
    {
        builder.Services.Configure<MvcOptions>(options => options.Filters.Add<UnitOfWorkFilter>());
        return builder;
    }

    /// <summary>
    /// 把所有已注册的 <see cref="DbContext"/> 派生类型补注册为 <see cref="DbContext"/> 服务。<para>
    /// 🔴 必须调用：EF 的 <c>AddDbContext&lt;T&gt;</c> 只注册具体类型 T，**不会**注册 <see cref="DbContext"/> 基类，
    /// 因此 <see cref="UnitOfWorkFilter"/> 里的 <c>GetServices&lt;DbContext&gt;()</c> 会拿到空集合，事务形同虚设。
    /// 在全部模块注册完成后调用本方法，才能真正做到「一个请求一个事务、跨模块统一提交」。
    /// </para>
    /// </summary>
    public static IServiceCollection RegisterDbContexts(this IServiceCollection services)
    {
        var contextTypes = services
            .Where(descriptor => typeof(DbContext).IsAssignableFrom(descriptor.ServiceType)
                                 && descriptor.ServiceType != typeof(DbContext))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToList();

        foreach (var contextType in contextTypes)
        {
            services.Add(new ServiceDescriptor(
                typeof(DbContext),
                serviceProvider => serviceProvider.GetRequiredService(contextType),
                ServiceLifetime.Scoped));
        }

        return services;
    }
}
