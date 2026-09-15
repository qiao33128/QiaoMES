using System.Globalization;
using QiaoMES.MasterData.Domain;
using QiaoMES.Shared;

namespace QiaoMES.MasterData.Application;

/// <summary>CSV 导入结果。</summary>
public record CsvImportResult(int Created, int Updated, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// 主数据 CSV 导入导出（产品 / 物料 / 工序 / 工作中心）。
/// <para>导入按「编码」upsert：不存在则新建，存在则更新。整批失败会返回逐行错误，不影响已成功行。</para>
/// </summary>
public interface ICatalogCsvService
{
    Task<Result<string>> ExportAsync(string resource, CancellationToken cancellationToken = default);

    Task<Result<CsvImportResult>> ImportAsync(string resource, string csvContent, CancellationToken cancellationToken = default);
}

public class CatalogCsvService(ICatalogRepository repository) : ICatalogCsvService
{
    private const string Products = "products";
    private const string Materials = "materials";
    private const string Operations = "operations";
    private const string WorkCenters = "work-centers";

    public async Task<Result<string>> ExportAsync(string resource, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeResource(resource);
        if (normalized is null)
        {
            return Result.Failure<string>(Unsupported(resource));
        }

        var builder = new System.Text.StringBuilder();

        switch (normalized)
        {
            case Products:
                builder.AppendLine(CsvSerializer.WriteLine(["code", "name", "spec", "unit", "remark", "isActive"]));
                foreach (var item in await LoadAllAsync<Product>(cancellationToken))
                {
                    builder.AppendLine(CsvSerializer.WriteLine(
                        [item.Code, item.Name, item.Spec, item.Unit, item.Remark, item.IsActive ? "1" : "0"]));
                }
                break;

            case Materials:
                builder.AppendLine(CsvSerializer.WriteLine(
                    ["code", "name", "materialType", "supplierPartNumber", "spec", "unit", "remark", "isActive"]));
                foreach (var item in await LoadAllAsync<Material>(cancellationToken))
                {
                    builder.AppendLine(CsvSerializer.WriteLine(
                    [
                        item.Code, item.Name, ((int)item.MaterialType).ToString(CultureInfo.InvariantCulture),
                        item.SupplierPartNumber, item.Spec, item.Unit, item.Remark, item.IsActive ? "1" : "0",
                    ]));
                }
                break;

            case Operations:
                builder.AppendLine(CsvSerializer.WriteLine(
                    ["code", "name", "standardSeconds", "isKeyOperation", "remark", "isActive"]));
                foreach (var item in await LoadAllAsync<Operation>(cancellationToken))
                {
                    builder.AppendLine(CsvSerializer.WriteLine(
                    [
                        item.Code, item.Name, item.StandardSeconds.ToString(CultureInfo.InvariantCulture),
                        item.IsKeyOperation ? "1" : "0", item.Remark, item.IsActive ? "1" : "0",
                    ]));
                }
                break;

            default:
                builder.AppendLine(CsvSerializer.WriteLine(["code", "name", "type", "workshop", "remark", "isActive"]));
                foreach (var item in await LoadAllAsync<WorkCenter>(cancellationToken))
                {
                    builder.AppendLine(CsvSerializer.WriteLine(
                    [
                        item.Code, item.Name, ((int)item.Type).ToString(CultureInfo.InvariantCulture),
                        item.Workshop, item.Remark, item.IsActive ? "1" : "0",
                    ]));
                }
                break;
        }

        return Result.Success(builder.ToString());
    }

    public async Task<Result<CsvImportResult>> ImportAsync(
        string resource,
        string csvContent,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeResource(resource);
        if (normalized is null)
        {
            return Result.Failure<CsvImportResult>(Unsupported(resource));
        }
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return Result.Failure<CsvImportResult>(Error.Validation("Csv.Empty", "CSV 内容为空"));
        }

        var rows = CsvSerializer.Parse(csvContent);
        if (rows.Count <= 1)
        {
            return Result.Failure<CsvImportResult>(Error.Validation("Csv.NoData", "CSV 只有表头，没有数据行"));
        }

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            try
            {
                var outcome = normalized switch
                {
                    Products => await UpsertProductAsync(row, cancellationToken),
                    Materials => await UpsertMaterialAsync(row, cancellationToken),
                    Operations => await UpsertOperationAsync(row, cancellationToken),
                    _ => await UpsertWorkCenterAsync(row, cancellationToken),
                };

                switch (outcome)
                {
                    case "created":
                        created++;
                        break;
                    case "updated":
                        updated++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (Exception exception)
            {
                errors.Add($"第 {index + 1} 行：{exception.Message}");
            }
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(new CsvImportResult(created, updated, skipped, errors));
    }

    // ---------------- 各资源的 upsert ----------------

    private async Task<string> UpsertProductAsync(IReadOnlyList<string> row, CancellationToken cancellationToken)
    {
        var code = Required(row, 0, "编码");
        var name = Required(row, 1, "名称");

        var existing = await repository.GetByCodeAsync<Product>(code, cancellationToken);
        if (existing is null)
        {
            var entity = new Product(code, name, Cell(row, 2), Cell(row, 3), Cell(row, 4));
            ApplyActiveState(entity, Cell(row, 5));
            repository.Add(entity);
            return "created";
        }

        existing.UpdateBasicInfo(name, Cell(row, 2), Cell(row, 3), Cell(row, 4));
        ApplyActiveState(existing, Cell(row, 5));
        return "updated";
    }

    private async Task<string> UpsertMaterialAsync(IReadOnlyList<string> row, CancellationToken cancellationToken)
    {
        var code = Required(row, 0, "编码");
        var name = Required(row, 1, "名称");
        var materialType = ParseMaterialType(Cell(row, 2));

        var existing = await repository.GetByCodeAsync<Material>(code, cancellationToken);
        if (existing is null)
        {
            var entity = new Material(code, name, materialType, Cell(row, 4), Cell(row, 5), Cell(row, 6));
            entity.UpdateMaterialInfo(materialType, Cell(row, 3));
            ApplyActiveState(entity, Cell(row, 7));
            repository.Add(entity);
            return "created";
        }

        existing.UpdateBasicInfo(name, Cell(row, 4), Cell(row, 5), Cell(row, 6));
        existing.UpdateMaterialInfo(materialType, Cell(row, 3));
        ApplyActiveState(existing, Cell(row, 7));
        return "updated";
    }

    private async Task<string> UpsertOperationAsync(IReadOnlyList<string> row, CancellationToken cancellationToken)
    {
        var code = Required(row, 0, "编码");
        var name = Required(row, 1, "名称");
        var standardSeconds = ParseInt(Cell(row, 2), 0, "标准工时");
        var isKey = ParseBool(Cell(row, 3));

        var existing = await repository.GetByCodeAsync<Operation>(code, cancellationToken);
        if (existing is null)
        {
            var entity = new Operation(code, name, standardSeconds, isKey, null, Cell(row, 4));
            ApplyActiveState(entity, Cell(row, 5));
            repository.Add(entity);
            return "created";
        }

        existing.UpdateBasicInfo(name, null, null, Cell(row, 4));
        existing.UpdateProcessInfo(standardSeconds, isKey, existing.DefaultWorkCenterId);
        ApplyActiveState(existing, Cell(row, 5));
        return "updated";
    }

    private async Task<string> UpsertWorkCenterAsync(IReadOnlyList<string> row, CancellationToken cancellationToken)
    {
        var code = Required(row, 0, "编码");
        var name = Required(row, 1, "名称");
        var type = ParseWorkCenterType(Cell(row, 2));

        var existing = await repository.GetByCodeAsync<WorkCenter>(code, cancellationToken);
        if (existing is null)
        {
            var entity = new WorkCenter(code, name, type, Cell(row, 3), Cell(row, 4));
            ApplyActiveState(entity, Cell(row, 5));
            repository.Add(entity);
            return "created";
        }

        existing.UpdateBasicInfo(name, null, null, Cell(row, 4));
        existing.UpdateLayout(type, Cell(row, 3), existing.ParentId);
        ApplyActiveState(existing, Cell(row, 5));
        return "updated";
    }

    // ---------------- 工具方法 ----------------

    private async Task<List<TEntity>> LoadAllAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : CatalogEntity
    {
        var result = new List<TEntity>();
        var page = 1;

        while (true)
        {
            var (items, totalCount) = await repository.QueryPagedAsync<TEntity>(
                new CatalogQuery { Page = page, PageSize = CatalogQuery.MaxPageSize }, cancellationToken);

            result.AddRange(items);

            if (items.Count == 0 || result.Count >= totalCount)
            {
                break;
            }

            page++;
        }

        return result;
    }

    private static void ApplyActiveState(CatalogEntity entity, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        entity.SetActive(ParseBool(value));
    }

    private static string? NormalizeResource(string resource)
    {
        var value = resource?.Trim().Trim('/').ToLowerInvariant();
        return value switch
        {
            Products => Products,
            Materials => Materials,
            Operations => Operations,
            WorkCenters => WorkCenters,
            _ => null,
        };
    }

    private static Error Unsupported(string resource)
        => Error.Validation("Csv.UnsupportedResource", $"不支持的主数据：{resource}（可用：products / materials / operations / work-centers）");

    private static string? Cell(IReadOnlyList<string> row, int index)
        => index < row.Count && !string.IsNullOrWhiteSpace(row[index]) ? row[index].Trim() : null;

    private static string Required(IReadOnlyList<string> row, int index, string fieldName)
        => Cell(row, index) ?? throw new InvalidOperationException($"{fieldName}不能为空");

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim() switch
        {
            "1" or "true" or "TRUE" or "是" or "启用" => true,
            "0" or "false" or "FALSE" or "否" or "停用" => false,
            _ => true,
        };
    }

    private static int ParseInt(string? value, int fallback, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value.Trim(), out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{fieldName}必须是整数");
    }

    private static MaterialType ParseMaterialType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return MaterialType.Raw;
        }

        var text = value.Trim();
        return text switch
        {
            "0" or "原材料" => MaterialType.Raw,
            "1" or "半成品" => MaterialType.SemiFinished,
            "2" or "成品" => MaterialType.Finished,
            "3" or "辅料" or "耗材" => MaterialType.Consumable,
            _ => MaterialType.Raw,
        };
    }

    private static WorkCenterType ParseWorkCenterType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return WorkCenterType.Line;
        }

        var text = value.Trim();
        return text switch
        {
            "0" or "产线" => WorkCenterType.Line,
            "1" or "单元" => WorkCenterType.Cell,
            "2" or "工位" => WorkCenterType.Station,
            "3" or "设备" => WorkCenterType.Machine,
            _ => WorkCenterType.Line,
        };
    }
}
