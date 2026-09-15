using QiaoMES.Shared;

namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 主数据实体的公共部分：编码、名称、规格、单位、启用状态、审计时间。
/// </summary>
public abstract class CatalogEntity : Entity
{
    protected CatalogEntity() { }

    protected CatalogEntity(string code, string name, string? spec = null, string? unit = null, string? remark = null)
        : base(Guid.NewGuid())
    {
        Code = code.Trim();
        Name = name.Trim();
        Spec = spec?.Trim();
        Unit = unit?.Trim();
        Remark = remark?.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>业务编码（全局唯一）。</summary>
    public string Code { get; protected set; } = string.Empty;

    /// <summary>名称。</summary>
    public string Name { get; protected set; } = string.Empty;

    /// <summary>规格 / 型号。</summary>
    public string? Spec { get; protected set; }

    /// <summary>计量单位。</summary>
    public string? Unit { get; protected set; }

    public string? Remark { get; protected set; }

    /// <summary>是否启用。停用后不应再被新工单 / 新工艺路线引用。</summary>
    public bool IsActive { get; protected set; }

    public DateTime CreatedAt { get; protected set; }

    public DateTime? UpdatedAt { get; protected set; }

    /// <summary>更新基础信息（编码不可修改，避免破坏历史引用）。</summary>
    public void UpdateBasicInfo(string name, string? spec, string? unit, string? remark)
    {
        Name = name.Trim();
        Spec = spec?.Trim();
        Unit = unit?.Trim();
        Remark = remark?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        UpdatedAt = DateTime.UtcNow;
    }
}
