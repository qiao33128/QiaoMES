using QiaoMES.Identity.Domain;
using QiaoMES.Shared.Authorization;

namespace QiaoMES.Domain.Tests;

/// <summary>角色与用户的授权关系维护。</summary>
public class IdentityDomainTests
{
    [Fact]
    public void 授予权限_幂等且不产生重复项()
    {
        var role = new Role("tester");

        role.GrantPermission(Permissions.WorkOrders.Read);
        role.GrantPermission(Permissions.WorkOrders.Read);

        Assert.Single(role.PermissionNames);
    }

    [Fact]
    public void 回收权限_幂等()
    {
        var role = new Role("tester");
        role.GrantPermission(Permissions.WorkOrders.Read);

        role.RevokePermission(Permissions.WorkOrders.Read);
        role.RevokePermission(Permissions.WorkOrders.Read);

        Assert.Empty(role.PermissionNames);
    }

    [Fact]
    public void 替换权限_新增缺失并回收多余()
    {
        var role = new Role("tester");
        role.GrantPermission(Permissions.WorkOrders.Read);

        role.ReplacePermissions([Permissions.WorkOrders.Create, "workorders:unknown"]);

        Assert.DoesNotContain(Permissions.WorkOrders.Read, role.PermissionNames);
        Assert.Contains(Permissions.WorkOrders.Create, role.PermissionNames);
        Assert.Equal(2, role.PermissionNames.Count);
    }

    [Fact]
    public void 替换角色_新增缺失并移除多余()
    {
        var user = new User("tester", "hash", "测试员");
        var keep = Guid.NewGuid();
        var remove = Guid.NewGuid();
        var add = Guid.NewGuid();

        user.ReplaceRoles([keep, remove]);
        Assert.Equal(2, user.RoleIds.Count);

        user.ReplaceRoles([keep, add]);

        Assert.Equal(2, user.RoleIds.Count);
        Assert.Contains(keep, user.RoleIds);
        Assert.Contains(add, user.RoleIds);
        Assert.DoesNotContain(remove, user.RoleIds);
    }

    [Fact]
    public void 停用用户_状态可切换()
    {
        var user = new User("tester", "hash", "测试员");
        Assert.True(user.IsActive);

        user.SetActive(false);

        Assert.False(user.IsActive);
    }

    [Fact]
    public void 记录登录_写入最后登录时间()
    {
        var user = new User("tester", "hash", "测试员");
        Assert.Null(user.LastLoginAt);

        user.RecordLogin();

        Assert.NotNull(user.LastLoginAt);
    }
}
