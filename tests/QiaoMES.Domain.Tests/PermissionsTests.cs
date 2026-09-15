using QiaoMES.Shared.Authorization;

namespace QiaoMES.Domain.Tests;

/// <summary>权限目录的一致性与自洽性。</summary>
public class PermissionsTests
{
    [Fact]
    public void 权限目录_非空且无重复()
    {
        Assert.NotEmpty(Permissions.All);
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// 防止「加了权限常量却忘了写界面元数据」，导致角色管理页面无法勾选该权限。
    /// </summary>
    [Fact]
    public void 展示目录_必须与权限常量一一对应()
    {
        var declared = Permissions.All.OrderBy(p => p, StringComparer.Ordinal).ToList();
        var documented = PermissionCatalog.All
            .Select(p => p.Code)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(declared, documented);
    }

    [Fact]
    public void 未登记的权限_应判定为未定义()
    {
        Assert.False(Permissions.IsDefined("workorders:not-exist"));
        Assert.True(Permissions.IsDefined(Permissions.WorkOrders.Read));
    }

    [Fact]
    public void 内置角色_不可被当作普通角色()
    {
        Assert.True(BuiltInRoles.IsBuiltIn(BuiltInRoles.Admin));
        Assert.False(BuiltInRoles.IsBuiltIn("custom-role"));
    }
}
