using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Domain.Tests;

/// <summary>工单聚合的状态机、工艺路线展开与工序报工（纯领域逻辑，不依赖数据库）。</summary>
public class WorkOrderTests
{
    private static WorkOrder CreateOrder(int plannedQuantity = 100)
        => new("WO-20260915-0001", Guid.NewGuid(), "P-001", "测试产品", plannedQuantity, null, null, "LINE-01", null);

    /// <summary>构造一个含若干工序的工艺路线快照。</summary>
    private static RoutingReleaseSnapshot CreateRouting(params string[] operationNames)
    {
        var steps = operationNames
            .Select((name, index) => new RoutingStepReleaseSnapshot(
                (index + 1) * 10,
                Guid.NewGuid(),
                $"OP{index + 1:D3}",
                name,
                null,
                60,
                false))
            .ToList();

        return new RoutingReleaseSnapshot(Guid.NewGuid(), "V1.0", null, null, steps);
    }

    private static WorkOrder CreateInProgressOrder(int plannedQuantity, params string[] operationNames)
    {
        var order = CreateOrder(plannedQuantity);
        Assert.True(order.Release(CreateRouting(operationNames.Length > 0 ? operationNames : ["印刷", "贴片"])).IsSuccess);
        Assert.True(order.StartProduction().IsSuccess);
        return order;
    }

    [Fact]
    public void 新建工单_状态为草稿_无工序任务()
    {
        var order = CreateOrder();

        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        Assert.Equal(0, order.CompletedQuantity);
        Assert.Empty(order.Operations);
    }

    [Fact]
    public void 下达_没有工艺路线时失败()
    {
        var order = CreateOrder();

        var result = order.Release(null);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void 下达_按工艺路线展开工序任务且顺序正确()
    {
        var order = CreateOrder();

        var result = order.Release(CreateRouting("印刷", "贴片", "回流焊"));

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkOrderStatus.Released, order.Status);

        var operations = order.OrderedOperations;
        Assert.Equal(3, operations.Count);
        Assert.Equal([10, 20, 30], operations.Select(o => o.Sequence));
        Assert.Equal("印刷", operations[0].OperationName);
        Assert.All(operations, o => Assert.Equal(WorkOrderOperationStatus.Pending, o.Status));
    }

    [Fact]
    public void 下达_重复下达失败()
    {
        var order = CreateOrder();
        order.Release(CreateRouting("印刷"));

        Assert.True(order.Release(CreateRouting("印刷")).IsFailure);
    }

    [Fact]
    public void 开始生产_必须先下达()
    {
        var order = CreateOrder();

        Assert.True(order.StartProduction().IsFailure);

        order.Release(CreateRouting("印刷"));

        Assert.True(order.StartProduction().IsSuccess);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
    }

    [Fact]
    public void 工序报工_累计达到计划数量时该工序完成()
    {
        var order = CreateInProgressOrder(10, "印刷");
        var operation = order.OrderedOperations[0];

        Assert.True(order.ReportOperation(operation.Id, 6, 0, 0).IsSuccess);
        Assert.Equal(WorkOrderOperationStatus.InProgress, operation.Status);

        Assert.True(order.ReportOperation(operation.Id, 4, 0, 0).IsSuccess);
        Assert.Equal(WorkOrderOperationStatus.Completed, operation.Status);
        Assert.Equal(10, operation.GoodQuantity);
        Assert.NotNull(operation.CompletedAt);
    }

    [Fact]
    public void 报工_超出计划数量被拒绝且不改变数据()
    {
        var order = CreateInProgressOrder(10, "印刷");
        var operation = order.OrderedOperations[0];

        Assert.True(order.ReportOperation(operation.Id, 11, 0, 0).IsFailure);
        Assert.Equal(0, operation.GoodQuantity);
    }

    [Fact]
    public void 报工_数量为零或负数被拒绝()
    {
        var order = CreateInProgressOrder(10, "印刷");
        var operation = order.OrderedOperations[0];

        Assert.True(order.ReportOperation(operation.Id, 0, 0, 0).IsFailure);
        Assert.True(order.ReportOperation(operation.Id, -1, 0, 0).IsFailure);
    }

    [Fact]
    public void 跳序报工_前序未完成时被拒绝()
    {
        var order = CreateInProgressOrder(10, "印刷", "贴片");
        var first = order.OrderedOperations[0];
        var second = order.OrderedOperations[1];

        var result = order.ReportOperation(second.Id, 10, 0, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);

        // 前序完成后即可报工
        Assert.True(order.ReportOperation(first.Id, 10, 0, 0).IsSuccess);
        Assert.True(order.ReportOperation(second.Id, 10, 0, 0).IsSuccess);
    }

    [Fact]
    public void 全部工序完成_工单自动完成且产量取最后一道工序良品数()
    {
        var order = CreateInProgressOrder(10, "印刷", "贴片");
        var first = order.OrderedOperations[0];
        var second = order.OrderedOperations[1];

        Assert.True(order.ReportOperation(first.Id, 10, 0, 0).IsSuccess);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);

        Assert.True(order.ReportOperation(second.Id, 9, 1, 0).IsSuccess);

        Assert.Equal(WorkOrderStatus.Completed, order.Status);
        Assert.Equal(9, order.CompletedQuantity);
        Assert.NotNull(order.CompletedAt);
    }

    [Fact]
    public void 不良与报废计入累计数量()
    {
        var order = CreateInProgressOrder(10, "印刷");
        var operation = order.OrderedOperations[0];

        Assert.True(order.ReportOperation(operation.Id, 5, 3, 2).IsSuccess);

        Assert.Equal(5, operation.GoodQuantity);
        Assert.Equal(3, operation.DefectQuantity);
        Assert.Equal(2, operation.ScrapQuantity);
        Assert.Equal(10, operation.ReportedQuantity);
        Assert.Equal(WorkOrderOperationStatus.Completed, operation.Status);
    }

    [Fact]
    public void 未开工的工单_不能报工()
    {
        var order = CreateOrder();
        order.Release(CreateRouting("印刷"));
        var operation = order.OrderedOperations[0];

        Assert.True(order.ReportOperation(operation.Id, 1, 0, 0).IsFailure);
    }

    [Fact]
    public void 不存在的工序任务_报工失败()
    {
        var order = CreateInProgressOrder(10, "印刷");

        var result = order.ReportOperation(Guid.NewGuid(), 1, 0, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public void 取消_已完成的工单不可取消()
    {
        var order = CreateInProgressOrder(5, "印刷");
        order.ReportOperation(order.OrderedOperations[0].Id, 5, 0, 0);

        Assert.True(order.Cancel().IsFailure);
        Assert.Equal(WorkOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void 更新计划_同时更新产品与计划字段()
    {
        var order = CreateOrder(10);
        var newProductId = Guid.NewGuid();

        order.UpdatePlan(newProductId, "P-002", "新产品", 20, DateTime.UnixEpoch, null, "LINE-02", "备注");

        Assert.Equal(newProductId, order.ProductId);
        Assert.Equal("P-002", order.ProductCode);
        Assert.Equal("新产品", order.ProductName);
        Assert.Equal(20, order.PlannedQuantity);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
    }
}
