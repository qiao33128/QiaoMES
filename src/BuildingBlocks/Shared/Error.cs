namespace QiaoMES.Shared;

/// <summary>
/// 错误类别。用于把业务失败统一映射为 HTTP 状态码，避免每个 Controller 自行判断。
/// </summary>
public enum ErrorType
{
    /// <summary>不可预期的业务失败（映射 500）。</summary>
    Failure = 0,

    /// <summary>入参校验失败（映射 400）。</summary>
    Validation = 1,

    /// <summary>资源不存在（映射 404）。</summary>
    NotFound = 2,

    /// <summary>状态冲突 / 并发冲突（映射 409）。</summary>
    Conflict = 3,

    /// <summary>未认证（映射 401）。</summary>
    Unauthorized = 4,

    /// <summary>无权限（映射 403）。</summary>
    Forbidden = 5,
}

/// <summary>
/// 表示操作失败的错误信息。
/// </summary>
public record Error(string Code, string Description, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "值不能为空", ErrorType.Validation);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    public static implicit operator Result(Error error) => Result.Failure(error);
}
