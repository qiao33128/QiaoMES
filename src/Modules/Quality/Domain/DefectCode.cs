using QiaoMES.Shared;

namespace QiaoMES.Quality.Domain;

/// <summary>
/// 不良代码（质量主数据）。检验项判不合格时选择，用于不良 TOP N 分析与改善。
/// </summary>
public class DefectCode : Entity
{
    private DefectCode() { }

    public DefectCode(string code, string name, string? category = null, string? description = null)
        : base(Guid.NewGuid())
    {
        Code = code.Trim();
        Name = name.Trim();
        Category = category?.Trim();
        Description = description?.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>不良代码（唯一）。</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>分类：外观 / 尺寸 / 功能 / 电气 / 包装…</summary>
    public string? Category { get; private set; }

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    public void Update(string name, string? category, string? description)
    {
        Name = name.Trim();
        Category = category?.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }
}
