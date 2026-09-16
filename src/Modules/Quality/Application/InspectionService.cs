using QiaoMES.Quality.Application.Contracts;
using QiaoMES.Quality.Domain;
using QiaoMES.Shared;
using QiaoMES.Shared.IntegrationEvents;

namespace QiaoMES.Quality.Application;

/// <summary>
/// 检验单服务：创建 → 录入检验项 → 判定；判定不合格时可自动生成不合格处置单。
/// </summary>
public interface IInspectionService
{
    Task<Result<PagedResult<InspectionDto>>> GetListAsync(InspectionQueryRequest query, CancellationToken cancellationToken = default);

    Task<Result<InspectionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<InspectionDto>> CreateAsync(CreateInspectionRequest request, CancellationToken cancellationToken = default);

    Task<Result<InspectionDto>> AddItemsAsync(Guid id, AddInspectionItemsRequest request, CancellationToken cancellationToken = default);

    Task<Result<InspectionDto>> RecordItemAsync(Guid id, RecordInspectionItemRequest request, CancellationToken cancellationToken = default);

    /// <summary>提交判定（不合格且勾选时自动创建处置单）。</summary>
    Task<Result<InspectionDto>> SubmitAsync(Guid id, SubmitInspectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>按 SN 查询检验历史（追溯用）。</summary>
    Task<Result<IReadOnlyList<InspectionDto>>> GetBySnAsync(string sn, CancellationToken cancellationToken = default);

    /// <summary>检验统计快照（大屏 / 报表用）。</summary>
    Task<Result<QualityStatsDto>> GetStatsAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);
}

public class InspectionService(
    IInspectionRepository repository,
    INonconformanceRepository nonconformanceRepository,
    IMaterialLotRepository materialLotRepository,
    IOutboxWriter outboxWriter,
    ICurrentUser currentUser) : IInspectionService
{
    public async Task<Result<PagedResult<InspectionDto>>> GetListAsync(
        InspectionQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = new InspectionQuery
        {
            Type = request.Type,
            Status = request.Status,
            Keyword = request.Keyword,
            WorkOrderId = request.WorkOrderId,
            From = request.From,
            To = request.To,
            Page = request.Page,
            PageSize = request.PageSize,
        };

        var (items, totalCount) = await repository.QueryAsync(query, cancellationToken);

        return Result.Success(new PagedResult<InspectionDto>
        {
            Items = items.Select(ToDto).ToList(),
            Page = query.NormalizedPage,
            PageSize = query.NormalizedPageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result<InspectionDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var inspection = await repository.GetByIdAsync(id, cancellationToken);
        return inspection is null ? NotFound() : Result.Success(ToDto(inspection));
    }

    public async Task<Result<InspectionDto>> CreateAsync(
        CreateInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SampleSize <= 0)
        {
            return Result.Failure<InspectionDto>(Error.Validation("Inspection.InvalidSampleSize", "抽样数必须大于 0"));
        }
        var hasTarget = request.WorkOrderId is not null
                        || request.MaterialId is not null
                        || !string.IsNullOrWhiteSpace(request.MaterialCode)
                        || !string.IsNullOrWhiteSpace(request.Sn);
        if (!hasTarget)
        {
            return Result.Failure<InspectionDto>(
                Error.Validation("Inspection.NoTarget", "必须指定工单、物料编码或 SN 中的至少一个受检对象"));
        }
        if (request.RejectedLimit <= request.AcceptedLimit)
        {
            return Result.Failure<InspectionDto>(
                Error.Validation("Inspection.InvalidAql", "拒收数 Re 必须大于允收数 Ac"));
        }

        var inspectionNumber = await repository.NextNumberAsync(request.Type, DateTime.Now, cancellationToken);

        var inspection = new Inspection(
            inspectionNumber,
            request.Type,
            request.SampleSize,
            request.WorkOrderId,
            request.WorkOrderOperationId,
            request.Sn,
            request.MaterialId,
            request.MaterialCode,
            request.LotNumber,
            request.ProductCode,
            request.AqlLevel,
            request.AcceptedLimit,
            request.RejectedLimit);

        if (request.Items is { Count: > 0 })
        {
            foreach (var item in request.Items)
            {
                inspection.AddItem(item.Name, item.Standard, item.LowerLimit, item.UpperLimit, item.IsKeyItem, item.Remark);
            }
        }

        // 新聚合：Add 会级联把检验项一并标记为新增
        repository.Add(inspection);
        await repository.SaveChangesAsync(cancellationToken);

        return Result.Success(ToDto(inspection));
    }

    public async Task<Result<InspectionDto>> AddItemsAsync(
        Guid id,
        AddInspectionItemsRequest request,
        CancellationToken cancellationToken = default)
    {
        var inspection = await repository.GetByIdAsync(id, cancellationToken);
        if (inspection is null)
        {
            return NotFound();
        }
        if (inspection.IsFinished)
        {
            return Result.Failure<InspectionDto>(Error.Conflict("Inspection.AlreadyJudged", "已判定的检验单不能再添加检验项"));
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Failure<InspectionDto>(Error.Validation("Inspection.NoItems", "至少需要一个检验项"));
        }

        foreach (var item in request.Items)
        {
            var created = inspection.AddItem(item.Name, item.Standard, item.LowerLimit, item.UpperLimit, item.IsKeyItem, item.Remark);
            // 聚合已被跟踪：新子实体必须显式 Add
            repository.AddItem(created);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(inspection));
    }

    public async Task<Result<InspectionDto>> RecordItemAsync(
        Guid id,
        RecordInspectionItemRequest request,
        CancellationToken cancellationToken = default)
    {
        var inspection = await repository.GetByIdAsync(id, cancellationToken);
        if (inspection is null)
        {
            return NotFound();
        }

        var result = inspection.RecordItem(
            request.ItemId,
            request.MeasuredValue,
            request.NumericValue,
            request.IsQualified,
            request.DefectCode,
            request.Remark);

        if (result.IsFailure)
        {
            return Result.Failure<InspectionDto>(result.Error);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(inspection));
    }

    public async Task<Result<InspectionDto>> SubmitAsync(
        Guid id,
        SubmitInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var inspection = await repository.GetByIdAsync(id, cancellationToken);
        if (inspection is null)
        {
            return NotFound();
        }

        var result = inspection.Submit(
            request.DefectQuantity,
            currentUser.UserId,
            currentUser.Username,
            request.Concession,
            request.Remark);

        if (result.IsFailure)
        {
            return Result.Failure<InspectionDto>(result.Error);
        }

        // IQC 判定后自动回写来料批次状态（上游谱系的准入口）
        if (inspection.Type == InspectionType.Iqc && !string.IsNullOrWhiteSpace(inspection.LotNumber))
        {
            var lot = await materialLotRepository.GetByLotNumberAsync(inspection.LotNumber!, cancellationToken);
            if (lot is not null)
            {
                lot.MarkInspected(
                    inspection.Status is InspectionStatus.Passed or InspectionStatus.Concessioned,
                    inspection.Id,
                    inspection.InspectionNumber,
                    inspection.Status == InspectionStatus.Concessioned
                        ? "IQC 让步接收"
                        : $"IQC {inspection.InspectionNumber} 判定不合格");
            }
        }

        // 判定不合格 → 按需自动派生不合格处置单
        if (inspection.NeedsDisposition && request.CreateNonconformance)
        {
            var firstDefect = inspection.OrderedItems.FirstOrDefault(i => i.IsQualified == false);
            var number = await nonconformanceRepository.NextNumberAsync(DateTime.Now, cancellationToken);

            var ncr = new Nonconformance(
                number,
                request.DefectQuantity > 0 ? request.DefectQuantity : 1,
                inspection.Id,
                inspection.WorkOrderId,
                inspection.Sn,
                firstDefect?.DefectCode,
                request.DefectDescription ?? firstDefect?.Name,
                inspection.ProductCode);

            nonconformanceRepository.Add(ncr);
        }

        // 判定完成 → 发布集成事件：与业务数据同一事务落库，由 Outbox 异步投递给订阅方
        // （质量模块不需要知道谁订阅，设备/看板/ERP 各自接入即可）
        var firstFailedItem = inspection.OrderedItems.FirstOrDefault(i => i.IsQualified == false);
        outboxWriter.Publish(new InspectionJudgedEvent(
            inspection.Id,
            inspection.InspectionNumber,
            (int)inspection.Type,
            (int)inspection.Status,
            inspection.Sn,
            inspection.ProductCode,
            inspection.LotNumber,
            inspection.WorkOrderId,
            firstFailedItem?.DefectCode,
            inspection.DefectQuantity,
            inspection.Conclusion.ToString()));

        // 两个仓储共享同一 DbContext（同一作用域），一次提交保持原子
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(inspection));
    }

    public async Task<Result<IReadOnlyList<InspectionDto>>> GetBySnAsync(
        string sn,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sn))
        {
            return Result.Failure<IReadOnlyList<InspectionDto>>(Error.Validation("Inspection.InvalidSn", "SN 不能为空"));
        }

        var items = await repository.GetBySnAsync(sn.Trim(), cancellationToken);
        return Result.Success<IReadOnlyList<InspectionDto>>(items.Select(ToDto).ToList());
    }

    public async Task<Result<QualityStatsDto>> GetStatsAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var counts = await repository.CountByStatusAsync(from, to, cancellationToken);
        var lookup = counts.ToDictionary(x => x.Status, x => (x.Count, x.DefectQuantity));

        var passed = lookup.GetValueOrDefault(InspectionStatus.Passed).Count;
        var failed = lookup.GetValueOrDefault(InspectionStatus.Failed).Count;
        var concessioned = lookup.GetValueOrDefault(InspectionStatus.Concessioned).Count;
        var pending = lookup.GetValueOrDefault(InspectionStatus.Pending).Count
                      + lookup.GetValueOrDefault(InspectionStatus.InProgress).Count;

        var judged = passed + failed + concessioned;
        var fpy = judged == 0 ? 0m : Math.Round((decimal)passed / judged * 100m, 2);

        return Result.Success(new QualityStatsDto(
            counts.Sum(x => x.Count),
            pending,
            passed,
            failed,
            concessioned,
            counts.Sum(x => x.DefectQuantity),
            fpy));
    }

    private static Result<InspectionDto> NotFound()
        => Result.Failure<InspectionDto>(Error.NotFound("Inspection.NotFound", "检验单不存在"));

    private static InspectionDto ToDto(Inspection inspection) => new(
        inspection.Id,
        inspection.InspectionNumber,
        inspection.Type,
        inspection.Status,
        inspection.Conclusion,
        inspection.WorkOrderId,
        inspection.WorkOrderOperationId,
        inspection.Sn,
        inspection.MaterialId,
        inspection.MaterialCode,
        inspection.LotNumber,
        inspection.ProductCode,
        inspection.SampleSize,
        inspection.AqlLevel,
        inspection.AcceptedLimit,
        inspection.RejectedLimit,
        inspection.DefectQuantity,
        inspection.InspectorId,
        inspection.InspectorName,
        inspection.CreatedAt,
        inspection.InspectedAt,
        inspection.Remark,
        inspection.FailedItemCount,
        inspection.IsFullyRecorded,
        inspection.OrderedItems.Select(ToItemDto).ToList());

    private static InspectionItemDto ToItemDto(InspectionItem item) => new(
        item.Id,
        item.Sequence,
        item.Name,
        item.Standard,
        item.LowerLimit,
        item.UpperLimit,
        item.IsKeyItem,
        item.MeasuredValue,
        item.NumericValue,
        item.IsQualified,
        item.DefectCode,
        item.Remark);
}
