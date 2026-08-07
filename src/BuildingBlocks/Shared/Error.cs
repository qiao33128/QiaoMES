namespace QiaoMES.Shared;

/// <summary>
/// 表示操作失败的错误信息。
/// </summary>
public record Error(string Code, string Description)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static readonly Error NullValue = new("Error.NullValue", "值不能为空");

    public static implicit operator Result(Error error) => Result.Failure(error);
}
