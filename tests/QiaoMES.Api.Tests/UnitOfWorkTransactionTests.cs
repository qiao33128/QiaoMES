using Microsoft.EntityFrameworkCore;
using Npgsql;
using QiaoMES.Identity.Domain;
using QiaoMES.Identity.Infrastructure.Persistence;
using QiaoMES.Production.Domain;
using QiaoMES.Production.Infrastructure.Persistence;

namespace QiaoMES.Api.Tests;

/// <summary>
/// 验证「一次请求内多个 DbContext 共享同一事务」这一关键约定：
/// 任一方失败回滚时，双方的数据都不应落库。
/// </summary>
[Collection(ApiCollection.Name)]
public class UnitOfWorkTransactionTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=qiaomes;Username=qiaomes;Password=qiaomes_dev";

    /// <summary>确保数据库结构就绪（CI 上可能是一个全新的空库）。</summary>
    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await using var identityDb = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
        await using var productionDb = new ProductionDbContext(
            new DbContextOptionsBuilder<ProductionDbContext>().UseNpgsql(connection).Options);

        await identityDb.Database.MigrateAsync();
        await productionDb.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task 同一事务下跨模块写入_回滚后双方都不落库()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var identityDb = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
        await using var productionDb = new ProductionDbContext(
            new DbContextOptionsBuilder<ProductionDbContext>().UseNpgsql(connection).Options);

        var roleId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await identityDb.Database.UseTransactionAsync(transaction);
            await productionDb.Database.UseTransactionAsync(transaction);

            var role = new Role($"tx-role-{Guid.NewGuid():N}", "事务测试角色");
            identityDb.Roles.Add(role);

            var workOrder = new WorkOrder(
                $"WO-TX-{Guid.NewGuid():N}"[..20], "P-TX", "事务测试产品", 1, null, null, null, null);
            productionDb.WorkOrders.Add(workOrder);

            await identityDb.SaveChangesAsync();
            await productionDb.SaveChangesAsync();

            roleId = role.Id;
            workOrderId = workOrder.Id;

            // 模拟后续步骤失败 → 整体回滚
            await transaction.RollbackAsync();
        }

        await using var verifyIdentityDb = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
        await using var verifyProductionDb = new ProductionDbContext(
            new DbContextOptionsBuilder<ProductionDbContext>().UseNpgsql(connection).Options);

        Assert.False(await verifyIdentityDb.Roles.AnyAsync(r => r.Id == roleId));
        Assert.False(await verifyProductionDb.WorkOrders.AnyAsync(w => w.Id == workOrderId));
    }

    [Fact]
    public async Task 同一事务下跨模块写入_提交后双方都落库()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var identityDb = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
        await using var productionDb = new ProductionDbContext(
            new DbContextOptionsBuilder<ProductionDbContext>().UseNpgsql(connection).Options);

        var roleId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await identityDb.Database.UseTransactionAsync(transaction);
            await productionDb.Database.UseTransactionAsync(transaction);

            var role = new Role($"tx-role-{Guid.NewGuid():N}", "事务测试角色");
            identityDb.Roles.Add(role);

            var workOrder = new WorkOrder(
                $"WO-TX-{Guid.NewGuid():N}"[..20], "P-TX", "事务测试产品", 1, null, null, null, null);
            productionDb.WorkOrders.Add(workOrder);

            await identityDb.SaveChangesAsync();
            await productionDb.SaveChangesAsync();

            roleId = role.Id;
            workOrderId = workOrder.Id;

            await transaction.CommitAsync();
        }

        await using var verifyIdentityDb = new IdentityDbContext(
            new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);
        await using var verifyProductionDb = new ProductionDbContext(
            new DbContextOptionsBuilder<ProductionDbContext>().UseNpgsql(connection).Options);

        Assert.True(await verifyIdentityDb.Roles.AnyAsync(r => r.Id == roleId));
        Assert.True(await verifyProductionDb.WorkOrders.AnyAsync(w => w.Id == workOrderId));

        // 清理测试数据
        var roleToRemove = await verifyIdentityDb.Roles.FirstAsync(r => r.Id == roleId);
        verifyIdentityDb.Roles.Remove(roleToRemove);
        var orderToRemove = await verifyProductionDb.WorkOrders.FirstAsync(w => w.Id == workOrderId);
        verifyProductionDb.WorkOrders.Remove(orderToRemove);
        await verifyIdentityDb.SaveChangesAsync();
        await verifyProductionDb.SaveChangesAsync();
    }
}
