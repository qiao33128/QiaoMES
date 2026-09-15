using QiaoMES.Production.Domain;
using QiaoMES.Shared;

namespace QiaoMES.Domain.Tests;

/// <summary>工单聚合的状态机与不变量（纯领域逻辑，不依赖数据库）。</summary>
public class WorkOrderTests
{
    private static WorkOrder CreateOrder(int plannedQuantity = 100)
        => new("WO-20260915-0001", "P-001", "测试产品", plannedQuantity, null, null, "LINE-01", null);

    private static WorkOrder CreateInProgressOrder(int plannedQuantity)
    {
        var order = CreateOrder(plannedQuantity);
        Assert.True(order.Release().IsSuccess);
        Assert.True(order.StartProduction().IsSuccess);
        return order;
    }

    [Fact]
    public void 新建工单_状态为草稿_完成数为零()
    {
        var order = CreateOrder();

        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        Assert.Equal(0, order.CompletedQuantity);
        Assert.Null(order.CompletedAt);
    }

    [Fact]
    public void 下达_草稿可下达_重复下达失败()
    {
        var order = CreateOrder();

        Assert.True(order.Release().IsSuccess);
        Assert.Equal(WorkOrderStatus.Released, order.Status);
        Assert.True(order.Release().IsFailure);
    }

    [Fact]
    public void 开始生产_必须先下达()
    {
        var order = CreateOrder();

        Assert.True(order.StartProduction().IsFailure);

        order.Release();

        Assert.True(order.StartProduction().IsSuccess);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void 报工_数量必须大于零(int quantity)
    {
        var order = CreateInProgressOrder(100);

        Assert.True(order.Report(quantity).IsFailure);
        Assert.Equal(0, order.CompletedQuantity);
    }

    [Fact]
    public void 报工_超出计划数量被拒绝且不改变完成数()
    {
        var order = CreateInProgressOrder(10);

        Assert.True(order.Report(11).IsFailure);
        Assert.Equal(0, order.CompletedQuantity);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);
    }

    [Fact]
    public void 报工_累计达到计划数量时自动完成()
    {
        var order = CreateInProgressOrder(10);

        Assert.True(order.Report(6).IsSuccess);
        Assert.Equal(WorkOrderStatus.InProgress, order.Status);

        Assert.True(order.Report(4).IsSuccess);
        Assert.Equal(WorkOrderStatus.Completed, order.Status);
        Assert.Equal(10, order.CompletedQuantity);
        Assert.NotNull(order.CompletedAt);
    }

    [Fact]
    public void 取消_已完成的工单不可取消()
    {
        var order = CreateInProgressOrder(5);
        order.Report(5);

        Assert.True(order.Cancel().IsFailure);
        Assert.Equal(WorkOrderStatus.Completed, order.Status);
    }

    [Fact]
    public void 取消_进行中的工单可以取消()
    {
        var order = CreateInProgressOrder(5);

        Assert.True(order.Cancel().IsSuccess);
        Assert.Equal(WorkOrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void 非法状态流转的错误类型为Conflict_以便统一映射为409()
    {
        var order = CreateOrder();

        var result = order.StartProduction();

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public void 数量校验失败的错误类型为Validation_以便统一映射为400()
    {
        var order = CreateInProgressOrder(10);

        var result = order.Report(0);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void 更新计划_只修改计划字段()
    {
        var order = CreateOrder(10);

        order.UpdatePlan("新名称", 20, DateTime.UnixEpoch, null, "LINE-02", "备注");

        Assert.Equal("新名称", order.ProductName);
        Assert.Equal(20, order.PlannedQuantity);
        Assert.Equal("LINE-02", order.WorkCenter);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
    }
}
