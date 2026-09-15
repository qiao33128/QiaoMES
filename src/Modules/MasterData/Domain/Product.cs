namespace QiaoMES.MasterData.Domain;

/// <summary>
/// 产品（可生产、可下达工单的对象）。
/// </summary>
public class Product : CatalogEntity
{
    private Product() { }

    public Product(string code, string name, string? spec = null, string? unit = null, string? remark = null)
        : base(code, name, spec, unit, remark)
    {
    }
}
