using QiaoMES.Shared;

namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 物料清单（BOM）。
/// <para>按「产品 + 版本」维度管理，同一产品同时只有一个生效版本。</para>
/// </summary>
public class Bom : Entity
{
    private Bom() { }

    public Bom(Guid productId, string version, string? remark = null)
        : base(Guid.NewGuid())
    {
        ProductId = productId;
        Version = version.Trim();
        Remark = remark?.Trim();
        IsActive = false;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid ProductId { get; private set; }

    /// <summary>版本号，如 <c>V1.0</c>。</summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>是否为该产品的生效版本。</summary>
    public bool IsActive { get; private set; }

    public string? Remark { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private readonly List<BomItem> _items = [];
    public IReadOnlyCollection<BomItem> Items => _items.AsReadOnly();

    /// <summary>
    /// 添加明细行。
    /// </summary>
    /// <returns>新建的明细；调用方在聚合已被跟踪时需显式持久化。</returns>
    public BomItem AddItem(Guid materialId, decimal quantity, string? unit, decimal lossRate, string? remark = null)
    {
        var item = new BomItem(Id, materialId, quantity, unit, lossRate, remark);
        _items.Add(item);
        Touch();
        return item;
    }

    /// <summary>移除明细行（软删除）。</summary>
    public Result RemoveItem(Guid itemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId && !i.IsDeleted);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Bom.ItemNotFound", "BOM 明细不存在"));
        }

        item.Delete();
        Touch();
        return Result.Success();
    }

    /// <summary>用给定集合整体替换明细。</summary>
    /// <returns>新增的明细列表，调用方需显式持久化。</returns>
    public IReadOnlyList<BomItem> ReplaceItems(IEnumerable<BomItemSpec> specs)
    {
        _items.Clear();

        var added = new List<BomItem>();
        foreach (var spec in specs)
        {
            added.Add(AddItem(spec.MaterialId, spec.Quantity, spec.Unit, spec.LossRate, spec.Remark));
        }

        return added;
    }

    public void UpdateRemark(string? remark)
    {
        Remark = remark?.Trim();
        Touch();
    }

    /// <summary>设为生效版本。</summary>
    public Result Activate()
    {
        if (_items.All(i => i.IsDeleted))
        {
            return Result.Failure(Error.Conflict("Bom.Empty", "BOM 没有明细，不能设为生效版本"));
        }

        IsActive = true;
        Touch();
        return Result.Success();
    }

    /// <summary>取消生效（历史版本）。</summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

/// <summary>BOM 明细行的输入规格。</summary>
public sealed record BomItemSpec(Guid MaterialId, decimal Quantity, string? Unit, decimal LossRate, string? Remark);

/// <summary>
/// BOM 明细行。
/// </summary>
public class BomItem : Entity
{
    private BomItem() { }

    public BomItem(Guid bomId, Guid materialId, decimal quantity, string? unit, decimal lossRate, string? remark = null)
        : base(Guid.NewGuid())
    {
        BomId = bomId;
        MaterialId = materialId;
        Quantity = quantity;
        Unit = unit?.Trim();
        LossRate = lossRate;
        Remark = remark?.Trim();
    }

    public Guid BomId { get; private set; }

    public Guid MaterialId { get; private set; }

    /// <summary>单位用量。</summary>
    public decimal Quantity { get; private set; }

    public string? Unit { get; private set; }

    /// <summary>损耗率（0.01 表示 1%）。</summary>
    public decimal LossRate { get; private set; }

    public string? Remark { get; private set; }

    /// <summary>含损耗的应领用量。</summary>
    public decimal RequiredQuantity => Quantity * (1 + LossRate);

    public void Update(decimal quantity, string? unit, decimal lossRate, string? remark)
    {
        Quantity = quantity;
        Unit = unit?.Trim();
        LossRate = lossRate;
        Remark = remark?.Trim();
    }
}
