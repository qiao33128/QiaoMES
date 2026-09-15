using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Quality.Application;

/// <summary>
/// 来料批次与批次谱系服务：批次维护 → IQC 准入 → SN 绑定批次 → 正向 / 反向追溯。
/// </summary>
public interface IMaterialLotService
{
    Task<Result<PagedResult<MaterialLotDto>>> GetListAsync(MaterialLotQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<MaterialLotDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按批次号取详情（含已流向的 SN 汇总）。</summary>
    Task<Result<MaterialLotDto>> GetByLotNumberAsync(string lotNumber, CancellationToken cancellationToken = default);

    Task<Result<MaterialLotDto>> CreateAsync(CreateMaterialLotRequest request, CancellationToken cancellationToken = default);

    /// <summary>登记 / 回写 IQC 结论，决定批次能否投产。</summary>
    Task<Result<MaterialLotDto>> InspectAsync(Guid id, InspectMaterialLotRequest request, CancellationToken cancellationToken = default);

    /// <summary>冻结 / 解冻批次。</summary>
    Task<Result<MaterialLotDto>> SetFrozenAsync(Guid id, FreezeMaterialLotRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// SN 绑定来料批次（支持批量、幂等）。绑定即扣减批次余量，建立「来料 → 成品」谱系。
    /// </summary>
    Task<Result<IReadOnlyList<SnMaterialConsumptionDto>>> BindConsumptionsAsync(
        BindMaterialConsumptionsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>正向追溯：某 SN 消耗了哪些来料批次。</summary>
    Task<Result<IReadOnlyList<SnMaterialConsumptionDto>>> GetConsumptionsBySnAsync(
        string sn,
        CancellationToken cancellationToken = default);

    /// <summary>反向追溯：某批次流向了哪些 SN（客诉定位同批影响范围）。</summary>
    Task<Result<MaterialLotTraceDto>> GetLotTraceAsync(
        string lotNumber,
        int take = 200,
        CancellationToken cancellationToken = default);
}

public class MaterialLotService(
    IMaterialLotRepository repository,
    ICurrentUser currentUser) : IMaterialLotService
{
    public async Task<Result<PagedResult<MaterialLotDto>>> GetListAsync(
        MaterialLotQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new MaterialLotQuery
        {
            Keyword = request.Keyword,
            MaterialCode = request.MaterialCode,
            Status = request.Status,
            From = request.From,
            To = request.To,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        // 列表用并行小查询补齐「已流向 SN 数 / 已消耗量」，避免 N+1 串行等待
        var dtos = await Task.WhenAll(items.Select(async lot =>
        {
            var (snCount, consumed) = await repository.GetLotConsumptionSummaryAsync(lot.LotNumber, cancellationToken);
            return ToDto(lot, snCount, consumed);
        }));

        return Result.Success(new PagedResult<MaterialLotDto>
        {
            Items = dtos,
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<MaterialLotDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var lot = await repository.GetByIdAsync(id, cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        var (snCount, consumed) = await repository.GetLotConsumptionSummaryAsync(lot.LotNumber, cancellationToken);
        return Result.Success(ToDto(lot, snCount, consumed));
    }

    public async Task<Result<MaterialLotDto>> GetByLotNumberAsync(string lotNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
        {
            return Result.Failure<MaterialLotDto>(Error.Validation("MaterialLot.InvalidLotNumber", "批次号不能为空"));
        }

        var lot = await repository.GetByLotNumberAsync(lotNumber.Trim(), cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        var (snCount, consumed) = await repository.GetLotConsumptionSummaryAsync(lot.LotNumber, cancellationToken);
        return Result.Success(ToDto(lot, snCount, consumed));
    }

    public async Task<Result<MaterialLotDto>> CreateAsync(
        CreateMaterialLotRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LotNumber))
        {
            return Result.Failure<MaterialLotDto>(Error.Validation("MaterialLot.InvalidLotNumber", "批次号不能为空"));
        }
        if (string.IsNullOrWhiteSpace(request.MaterialCode))
        {
            return Result.Failure<MaterialLotDto>(Error.Validation("MaterialLot.InvalidMaterial", "物料编码不能为空"));
        }
        if (request.Quantity <= 0)
        {
            return Result.Failure<MaterialLotDto>(Error.Validation("MaterialLot.InvalidQuantity", "来料数量必须大于 0"));
        }

        var lotNumber = request.LotNumber.Trim();
        if (await repository.IsLotNumberTakenAsync(lotNumber, null, cancellationToken))
        {
            return Result.Failure<MaterialLotDto>(Error.Conflict("MaterialLot.LotNumberTaken", $"批次号 {lotNumber} 已存在"));
        }

        var lot = new MaterialLot(
            lotNumber,
            request.MaterialCode,
            request.Quantity,
            request.MaterialName,
            request.Supplier,
            request.SupplierLotNumber,
            request.Unit,
            request.ReceivedAt,
            request.Remark);

        repository.Add(lot);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(lot, 0, 0m));
    }

    public async Task<Result<MaterialLotDto>> InspectAsync(
        Guid id,
        InspectMaterialLotRequest request,
        CancellationToken cancellationToken = default)
    {
        var lot = await repository.GetByIdAsync(id, cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        lot.MarkInspected(request.Passed, request.InspectionId, request.InspectionNumber, request.Reason);
        await repository.SaveChangesAsync(cancellationToken);

        var (snCount, consumed) = await repository.GetLotConsumptionSummaryAsync(lot.LotNumber, cancellationToken);
        return Result.Success(ToDto(lot, snCount, consumed));
    }

    public async Task<Result<MaterialLotDto>> SetFrozenAsync(
        Guid id,
        FreezeMaterialLotRequest request,
        CancellationToken cancellationToken = default)
    {
        var lot = await repository.GetByIdAsync(id, cancellationToken);
        if (lot is null)
        {
            return NotFound();
        }

        var result = lot.SetFrozen(request.Frozen, request.Reason);
        if (result.IsFailure)
        {
            return Result.Failure<MaterialLotDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);

        var (snCount, consumed) = await repository.GetLotConsumptionSummaryAsync(lot.LotNumber, cancellationToken);
        return Result.Success(ToDto(lot, snCount, consumed));
    }

    public async Task<Result<IReadOnlyList<SnMaterialConsumptionDto>>> BindConsumptionsAsync(
        BindMaterialConsumptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Failure<IReadOnlyList<SnMaterialConsumptionDto>>(
                Error.Validation("MaterialLot.NoItems", "至少需要一条绑定记录"));
        }

        var created = new List<SnMaterialConsumption>();
        var skipped = new List<SnMaterialConsumptionDto>();

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Sn) || string.IsNullOrWhiteSpace(item.MaterialCode) || string.IsNullOrWhiteSpace(item.LotNumber))
            {
                return Result.Failure<IReadOnlyList<SnMaterialConsumptionDto>>(
                    Error.Validation("MaterialLot.InvalidBinding", "SN / 物料编码 / 批次号不能为空"));
            }
            if (item.Quantity <= 0)
            {
                return Result.Failure<IReadOnlyList<SnMaterialConsumptionDto>>(
                    Error.Validation("MaterialLot.InvalidQuantity", "消耗数量必须大于 0"));
            }

            var lot = await repository.GetByLotNumberAsync(item.LotNumber.Trim(), cancellationToken);
            if (lot is null)
            {
                return Result.Failure<IReadOnlyList<SnMaterialConsumptionDto>>(
                    Error.NotFound("MaterialLot.NotFound", $"批次 {item.LotNumber} 不存在"));
            }

            // 幂等：同一 SN + 批次 + 物料只绑一次（重复提交不重复扣减）
            var exists = await repository.ConsumptionExistsAsync(
                item.Sn.Trim(), lot.LotNumber, item.MaterialCode.Trim(), cancellationToken);
            if (exists)
            {
                continue;
            }

            var consumeResult = lot.Consume(item.Quantity);
            if (consumeResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<SnMaterialConsumptionDto>>(consumeResult.Error);
            }

            var consumption = new SnMaterialConsumption(
                item.Sn,
                item.MaterialCode,
                lot.LotNumber,
                item.Quantity,
                item.WorkOrderId,
                item.WorkOrderOperationId,
                item.OperationName,
                item.EquipmentCode,
                currentUser.UserId,
                item.Remark);

            repository.AddConsumption(consumption);
            created.Add(consumption);
        }

        await repository.SaveChangesAsync(cancellationToken);
        _ = skipped;

        return Result.Success<IReadOnlyList<SnMaterialConsumptionDto>>(created.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<SnMaterialConsumptionDto>>> GetConsumptionsBySnAsync(
        string sn,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sn))
        {
            return Result.Failure<IReadOnlyList<SnMaterialConsumptionDto>>(
                Error.Validation("MaterialLot.InvalidSn", "SN 不能为空"));
        }

        var items = await repository.GetConsumptionsBySnAsync(sn.Trim(), cancellationToken);
        return Result.Success<IReadOnlyList<SnMaterialConsumptionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<MaterialLotTraceDto>> GetLotTraceAsync(
        string lotNumber,
        int take = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(lotNumber))
        {
            return Result.Failure<MaterialLotTraceDto>(Error.Validation("MaterialLot.InvalidLotNumber", "批次号不能为空"));
        }

        var lot = await repository.GetByLotNumberAsync(lotNumber.Trim(), cancellationToken);
        if (lot is null)
        {
            return Result.Failure<MaterialLotTraceDto>(Error.NotFound("MaterialLot.NotFound", $"批次 {lotNumber} 不存在"));
        }

        var max = take is < 1 or > 1000 ? 200 : take;
        var consumptions = await repository.GetConsumptionsByLotAsync(lot.LotNumber, max, cancellationToken);
        var (snCount, consumed) = await repository.GetLotConsumptionSummaryAsync(lot.LotNumber, cancellationToken);

        return Result.Success(new MaterialLotTraceDto(
            ToDto(lot, snCount, consumed),
            snCount,
            consumed,
            consumptions.Select(ToDto).ToList()));
    }

    private static Result<MaterialLotDto> NotFound()
        => Result.Failure<MaterialLotDto>(Error.NotFound("MaterialLot.NotFound", "来料批次不存在"));

    private static MaterialLotDto ToDto(MaterialLot lot, int consumedSnCount, decimal consumedQuantity) => new(
        lot.Id,
        lot.LotNumber,
        lot.MaterialCode,
        lot.MaterialName,
        lot.Supplier,
        lot.SupplierLotNumber,
        lot.Quantity,
        lot.RemainingQuantity,
        lot.Unit,
        lot.ReceivedAt,
        lot.Status,
        lot.StatusReason,
        lot.IqcInspectionId,
        lot.IqcInspectionNumber,
        lot.InspectedAt,
        lot.Remark,
        lot.CreatedAt,
        lot.UpdatedAt,
        consumedSnCount,
        consumedQuantity);

    private static SnMaterialConsumptionDto ToDto(SnMaterialConsumption consumption) => new(
        consumption.Id,
        consumption.Sn,
        consumption.MaterialCode,
        consumption.LotNumber,
        consumption.Quantity,
        consumption.WorkOrderId,
        consumption.WorkOrderOperationId,
        consumption.OperationName,
        consumption.EquipmentCode,
        consumption.OperatorId,
        consumption.BoundAt,
        consumption.Remark);
}
