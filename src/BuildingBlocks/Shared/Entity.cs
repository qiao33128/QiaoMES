using System.ComponentModel.DataAnnotations;

namespace QiaoMES.Shared;

/// <summary>
/// 领域实体基类。提供统一的 Id 和软删除支持。
/// </summary>
public abstract class Entity
{
    protected Entity() { }

    protected Entity(Guid id) => Id = id;

    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>是否已软删除（逻辑删除）。</summary>
    public bool IsDeleted { get; protected set; }

    /// <summary>软删除标记。</summary>
    public void Delete() => IsDeleted = true;

    /// <summary>恢复删除。</summary>
    public void Restore() => IsDeleted = false;
}
