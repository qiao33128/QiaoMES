using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QiaoMES.Shared;

namespace QiaoMES.Infrastructure.Http;

/// <summary>错误类别 → HTTP 状态码 / ProblemDetails 的统一映射。</summary>
public static class ErrorMappingExtensions
{
    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static ProblemDetails ToProblemDetails(this Error error, string? instance = null)
    {
        var status = error.Type.ToStatusCode();
        var problem = new ProblemDetails
        {
            Title = error.Description,
            Status = status,
            Type = $"https://httpstatuses.io/{status}",
            Instance = instance,
        };
        // 机器可读的业务错误码，前端无需解析文案
        problem.Extensions["code"] = error.Code;
        return problem;
    }
}

/// <summary>
/// 把 <see cref="Result"/> 转换为统一格式的 <see cref="IActionResult"/>。
/// Controller 不再手写 BadRequest / NotFound 分支。
/// </summary>
public static class ApiResults
{
    public static IActionResult FromResult(Result result)
        => result.IsSuccess ? new OkResult() : Problem(result.Error);

    public static IActionResult FromResult<TValue>(Result<TValue> result)
        => result.IsSuccess ? new OkObjectResult(result.Value) : Problem(result.Error);

    public static IActionResult Problem(Error error, string? instance = null)
        => new ObjectResult(error.ToProblemDetails(instance))
        {
            StatusCode = error.Type.ToStatusCode(),
            ContentTypes = { "application/problem+json" },
        };
}
