namespace QiaoMES.Assistant.Application.Contracts;

/// <summary>一次提问。<paramref name="PreviousSql"/> 用于支持「再按产线分组」这类追问。</summary>
public sealed record AskQuestionRequest(
    string Question,
    int? MaxRows = null,
    string? PreviousQuestion = null,
    string? PreviousSql = null);

/// <summary>结果集列元数据。</summary>
public sealed record QueryColumnDto(string Name, string DataType);

/// <summary>只读查询结果。</summary>
public sealed record QueryResultDto(
    IReadOnlyList<QueryColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int RowCount,
    bool Truncated,
    long ElapsedMs)
{
    public static QueryResultDto Empty { get; } = new([], [], 0, false, 0);
}

/// <summary>执行结果(用返回值而不是异常表达"SQL 跑不通",便于驱动自我修复)。</summary>
public sealed record QueryExecutionOutcome(QueryResultDto? Result, string? Error, bool IsInfrastructure = false)
{
    public bool IsSuccess => Error is null && Result is not null;

    public static QueryExecutionOutcome Ok(QueryResultDto result) => new(result, null);

    /// <param name="isInfrastructure">
    /// true 表示这是**执行环境**的问题（连接断开 / 协议错乱 / 连不上 / 超时），而不是模型把 SQL 写错了。
    /// <para>
    /// 这个区分很重要：环境问题把报错回灌给模型重写 SQL 是**没有意义**的 —— 它会写出一样的 SQL、
    /// 再失败一次，白烧两次模型调用，还让用户以为是"问不出来"。
    /// </para>
    /// </param>
    public static QueryExecutionOutcome Failed(string error, bool isInfrastructure = false)
        => new(null, error, isInfrastructure);
}

/// <summary>模型给出的 SQL 与展示建议。</summary>
public sealed record SqlGenerationResult(
    string? Sql,
    string Chart,
    string? XField,
    string? YField,
    string? Explanation,
    string? Thought,
    string? Model,
    /// <summary>非空表示"调用/解析失败"(区别于模型主动回答"这个问题答不了")。</summary>
    string? Error = null)
{
    public bool HasSql => !string.IsNullOrWhiteSpace(Sql);

    /// <summary>模型主动表示"用现有数据回答不了"。</summary>
    public static SqlGenerationResult Unavailable(string explanation, string? model = null)
        => new(null, "table", null, null, explanation, null, model);

    /// <summary>技术性失败(网络 / 鉴权 / 解析)。</summary>
    public static SqlGenerationResult Failed(string error, string? model = null)
        => new(null, "table", null, null, null, null, model, error);
}

/// <summary>生成 SQL 的输入。</summary>
public sealed record SqlGenerationRequest(
    string Question,
    string SchemaText,
    string GlossaryText,
    string? PreviousQuestion = null,
    string? PreviousSql = null);

/// <summary>问数结果。</summary>
public sealed record AnswerDto(
    string Question,
    bool Answered,
    string? Sql,
    string? Explanation,
    string? Thought,
    string Chart,
    string? XField,
    string? YField,
    IReadOnlyList<QueryColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    int RowCount,
    bool Truncated,
    long ElapsedMs,
    int Attempts,
    string? Model,
    string? Failure);

/// <summary>问数能力自检(前端据此提示"还没配模型")。</summary>
public sealed record AssistantStatusDto(
    bool Enabled,
    bool LlmConfigured,
    string? Model,
    bool HasDedicatedReadOnlyConnection,
    int MaxRows,
    int QueryTimeoutSeconds,
    int TableCount,
    string Hint);
