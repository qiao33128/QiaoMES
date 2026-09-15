namespace QiaoMES.Shared.Authorization;

/// <summary>权限的展示信息（分组 + 中文名称），用于角色管理界面。</summary>
public sealed record PermissionDescriptor(string Group, string Code, string Name);

/// <summary>
/// 权限目录的展示信息。
/// <para>
/// 与 <see cref="Permissions"/> 中的常量一一对应（有单元测试保证不遗漏），
/// 仅承担「给界面看的元数据」，不参与鉴权判断。
/// </para>
/// </summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDescriptor> All { get; } =
    [
        new("工单", Permissions.WorkOrders.Read, "查看工单"),
        new("工单", Permissions.WorkOrders.Create, "创建工单"),
        new("工单", Permissions.WorkOrders.Update, "编辑工单"),
        new("工单", Permissions.WorkOrders.Release, "下达工单"),
        new("工单", Permissions.WorkOrders.Start, "开始生产"),
        new("工单", Permissions.WorkOrders.Report, "生产报工"),
        new("工单", Permissions.WorkOrders.Complete, "完成工单"),
        new("工单", Permissions.WorkOrders.Cancel, "取消工单"),
        new("用户", Permissions.Users.Read, "查看用户"),
        new("用户", Permissions.Users.Manage, "管理用户"),
        new("角色", Permissions.Roles.Read, "查看角色"),
        new("角色", Permissions.Roles.Manage, "管理角色"),
    ];

    /// <summary>按分组归集，便于前端直接渲染。</summary>
    public static IReadOnlyList<PermissionGroup> Groups { get; } = All
        .GroupBy(p => p.Group)
        .Select(g => new PermissionGroup(g.Key, g.ToList()))
        .ToList();
}

/// <summary>权限分组。</summary>
public sealed record PermissionGroup(string Group, IReadOnlyList<PermissionDescriptor> Items);
