using Microsoft.Extensions.Logging;
using QiaoMES.Assistant.Application.Contracts;
using QiaoMES.Assistant.Domain;

namespace QiaoMES.Assistant.Application;

/// <summary>
/// 智能问数编排:语义层 → 大模型生成 SQL → 只读护栏 → 只读执行 → (失败则带着错误自我修复)。
/// <para>
/// 之所以要"修复回路":模型写错列名或漏了软删除条件是常态,直接把错误原样抛给用户,
/// 体验会非常差。把 PostgreSQL 的报错回灌一次,成功率通常能从 ~70% 提到 ~95%。
/// </para>
/// </summary>
public sealed class AssistantService(
    AssistantOptions options,
    ISchemaProvider schemaProvider,
    ISqlGenerator sqlGenerator,
    IReadOnlyQueryRunner queryRunner,
    ILogger<AssistantService> logger) : IAssistantService
{
    public async Task<AnswerDto> AskAsync(AskQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return Failure(question, 0, "问题不能为空");
        }

        if (!options.Enabled)
        {
            return Failure(question, 0, "智能问数未启用(配置项 Assistant:Enabled = false)");
        }

        if (!sqlGenerator.IsConfigured)
        {
            return Failure(question, 0,
                "未配置大模型(请在 appsettings 的 Assistant:Llm 里填写 BaseUrl / ApiKey / Model,详见 docs/AI-QUERY.md)");
        }

        var maxRows = Math.Clamp(request.MaxRows ?? options.MaxRows, 1, 2000);
        var generationRequest = new SqlGenerationRequest(
            question,
            await schemaProvider.GetSchemaTextAsync(cancellationToken),
            await schemaProvider.GetGlossaryTextAsync(cancellationToken),
            request.PreviousQuestion,
            request.PreviousSql);

        var generation = await sqlGenerator.GenerateAsync(generationRequest, cancellationToken);
        var attempts = 1;

        if (!generation.HasSql)
        {
            // 两种情况:模型主动说"答不了"(正常结果),或模型调用本身失败(技术错误)
            var failed = generation.Error is not null;
            return new AnswerDto(
                question, false, null,
                generation.Explanation ?? (failed ? "没能生成查询。" : "这个问题无法用当前数据回答。"),
                generation.Thought, generation.Chart, generation.XField, generation.YField,
                [], [], 0, false, 0, attempts, generation.Model, generation.Error);
        }

        var maxAttempts = Math.Clamp(options.MaxRepairAttempts, 0, 5) + 1;
        string? lastFailure = null;

        for (var round = 1; round <= maxAttempts; round++)
        {
            var validation = SqlGuard.Validate(generation.Sql);
            if (validation.IsValid)
            {
                var guarded = SqlGuard.WrapWithLimit(validation.NormalizedSql!, maxRows);
                var outcome = await queryRunner.ExecuteAsync(guarded, maxRows, cancellationToken);

                if (outcome.IsSuccess)
                {
                    var result = outcome.Result!;
                    logger.LogInformation(
                        "智能问数成功:问题={Question} 行数={Rows} 耗时={Elapsed}ms 修复轮次={Round}",
                        question, result.RowCount, result.ElapsedMs, round - 1);

                    return new AnswerDto(
                        question, true, validation.NormalizedSql!,
                        generation.Explanation, generation.Thought,
                        generation.Chart, generation.XField, generation.YField,
                        result.Columns, result.Rows, result.RowCount, result.Truncated, result.ElapsedMs,
                        attempts, generation.Model, null);
                }

                lastFailure = outcome.Error;
            }
            else
            {
                lastFailure = $"SQL 未通过只读安全校验:{validation.Reason}";
            }

            logger.LogWarning("智能问数第 {Round} 轮失败:{Failure}", round, lastFailure);

            if (round == maxAttempts)
            {
                break;
            }

            generation = await sqlGenerator.RepairAsync(generationRequest, generation.Sql!, lastFailure!, cancellationToken);
            attempts++;

            if (!generation.HasSql)
            {
                break;
            }
        }

        return Failure(question, attempts, lastFailure ?? "无法生成可执行的查询", generation);
    }

    public async Task<AssistantStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var tableCount = await schemaProvider.GetTableCountAsync(cancellationToken);
        var llmConfigured = sqlGenerator.IsConfigured;

        var hint = !options.Enabled
            ? "智能问数已关闭:把配置项 Assistant:Enabled 设为 true 即可开启。"
            : llmConfigured
                ? "已就绪。可以直接用中文提问,例如「最近 7 天各产线的良率是多少」。"
                : "还没配置大模型:在 appsettings 的 Assistant:Llm 里填写 BaseUrl / ApiKey / Model。"
                  + "任何 OpenAI 兼容端点都行(DeepSeek / 通义千问 / Kimi / 本地 Ollama),详见 docs/AI-QUERY.md。";

        return new AssistantStatusDto(
            options.Enabled,
            llmConfigured,
            llmConfigured ? sqlGenerator.Model : null,
            queryRunner.UsesDedicatedConnection,
            options.MaxRows,
            options.QueryTimeoutSeconds,
            tableCount,
            hint);
    }

    private static AnswerDto Failure(string question, int attempts, string failure, SqlGenerationResult? generation = null)
        => new(
            question, false, null,
            "没能给出可执行的查询。", generation?.Thought,
            generation?.Chart ?? "table", generation?.XField, generation?.YField,
            [], [], 0, false, 0, attempts, generation?.Model, failure);
}
