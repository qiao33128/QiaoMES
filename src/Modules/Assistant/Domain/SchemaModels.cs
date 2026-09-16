namespace QiaoMES.Assistant.Domain;

/// <summary>
/// 语义层里的一列。<para>
/// 关键点是「业务名 + 说明 + 枚举取值」——把 <c>Status = 3</c> 翻译成「已完工」,
/// 模型才可能写出正确的 WHERE 与 GROUP BY,这是 Text-to-SQL 效果好坏的分水岭。
/// </para>
/// </summary>
public sealed record ColumnSchema(
    string Name,
    string DataType,
    string BusinessName,
    string? Description = null,
    IReadOnlyDictionary<int, string>? EnumValues = null,
    bool IsSensitive = false);

/// <summary>语义层里的一张表。</summary>
public sealed record TableSchema(
    string Schema,
    string Table,
    string BusinessName,
    string Description,
    IReadOnlyList<ColumnSchema> Columns,
    IReadOnlyList<string> Keywords)
{
    /// <summary>带 schema 的全限定名(写 SQL 时必须用它)。</summary>
    public string QualifiedName => $"{Schema}.{Table}";
}

/// <summary>只读 SQL 校验结果。</summary>
public sealed record SqlValidationResult(bool IsValid, string? Reason, string? NormalizedSql)
{
    public static SqlValidationResult Ok(string normalizedSql) => new(true, null, normalizedSql);

    public static SqlValidationResult Invalid(string reason) => new(false, reason, null);
}
