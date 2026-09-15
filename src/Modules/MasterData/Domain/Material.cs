namespace QiaoMES.MasterData.Domain;

/// <summary>物料类型。</summary>
public enum MaterialType
{
    /// <summary>原材料。</summary>
    Raw = 0,

    /// <summary>半成品。</summary>
    SemiFinished = 1,

    /// <summary>成品。</summary>
    Finished = 2,

    /// <summary>辅料 / 耗材（锡膏、胶水等）。</summary>
    Consumable = 3,
}

/// <summary>
/// 物料。BOM 的组成单元，也是上料防错校验的对象。
/// </summary>
public class Material : CatalogEntity
{
    private Material() { }

    public Material(
        string code,
        string name,
        MaterialType materialType = MaterialType.Raw,
        string? spec = null,
        string? unit = null,
        string? remark = null)
        : base(code, name, spec, unit, remark)
    {
        MaterialType = materialType;
    }

    public MaterialType MaterialType { get; private set; }

    /// <summary>供应商料号（可选，用于与 ERP / 采购对齐）。</summary>
    public string? SupplierPartNumber { get; private set; }

    public void UpdateMaterialInfo(MaterialType materialType, string? supplierPartNumber)
    {
        MaterialType = materialType;
        SupplierPartNumber = supplierPartNumber?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
