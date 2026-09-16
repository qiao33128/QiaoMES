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
public sealed record QueryExecutionOutcome(QueryResultDto? Result, string? Error)
{
    public bool IsSuccess => Error is null && Result is not null;

    public static QueryExecutionOutcome Ok(QueryResultDto result) => new(result, null);

    public static QueryExecutionOutcome Failed(string error) => new(null, error);
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
