using QiaoMES.Assistant.Application.Contracts;

namespace QiaoMES.Assistant.Application;

/// <summary>
/// 语义层提供者:把「数据库真实列」与「业务注解(中文名 / 枚举取值 / 口径)」合成
/// 一段可直接放进提示词的文本。<para>
/// 真实列从 <c>information_schema.columns</c> 读,保证不会讲错列名;业务注解来自内置目录。
/// </para>
/// </summary>
public interface ISchemaProvider
{
    /// <summary>表结构描述(带缓存)。</summary>
    Task<string> GetSchemaTextAsync(CancellationToken cancellationToken = default);

    /// <summary>业务口径与使用规则(枚举含义、班次定义、指标公式、软删除等)。</summary>
    Task<string> GetGlossaryTextAsync(CancellationToken cancellationToken = default);

    /// <summary>语义层覆盖的表数量。</summary>
    Task<int> GetTableCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>自然语言 → SQL 的生成器(大模型适配层)。</summary>
public interface ISqlGenerator
{
    /// <summary>是否已配置可用(没配 Key 时前端要给出明确提示)。</summary>
    bool IsConfigured { get; }

    string Model { get; }

    Task<SqlGenerationResult> GenerateAsync(SqlGenerationRequest request, CancellationToken cancellationToken = default);

    /// <summary>把执行失败的原因回灌给模型,让它改一版。</summary>
    Task<SqlGenerationResult> RepairAsync(
        SqlGenerationRequest request,
        string failedSql,
        string error,
        CancellationToken cancellationToken = default);
}

/// <summary>只读执行器:以 <c>READ ONLY</c> 事务跑 SQL,带超时与结果集上界。</summary>
public interface IReadOnlyQueryRunner
{
    /// <summary>问数是否使用了独立的只读连接串。</summary>
    bool UsesDedicatedConnection { get; }

    Task<QueryExecutionOutcome> ExecuteAsync(string sql, int maxRows, CancellationToken cancellationToken = default);
}

/// <summary>智能问数应用服务。</summary>
public interface IAssistantService
{
    Task<AnswerDto> AskAsync(AskQuestionRequest request, CancellationToken cancellationToken = default);

    Task<AssistantStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
