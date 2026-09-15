using System.Reflection;

namespace QiaoMES.Shared.Authorization;

/// <summary>
/// 权限目录（单一事实来源）。
/// <para>命名规范：<c>资源:动作</c>，全小写，用冒号分隔。新增权限只需在此登记常量。</para>
/// </summary>
public static class Permissions
{
    /// <summary>工单相关权限。</summary>
    public static class WorkOrders
    {
        public const string Read = "workorders:read";
        public const string Create = "workorders:create";
        public const string Update = "workorders:update";
        public const string Release = "workorders:release";
        public const string Start = "workorders:start";
        public const string Report = "workorders:report";
        public const string Complete = "workorders:complete";
        public const string Cancel = "workorders:cancel";
    }

    /// <summary>主数据（产品 / 物料 / 工序 / 工作中心 / 工艺路线）权限。</summary>
    public static class MasterData
    {
        public const string Read = "masterdata:read";
        public const string Manage = "masterdata:manage";
    }

    /// <summary>质量模块权限（检验、不合格处置、不良代码）。</summary>
    public static class Quality
    {
        /// <summary>查看检验单 / 处置单 / 不良代码 / SPC。</summary>
        public const string Read = "quality:read";

        /// <summary>执行检验（建单、录入、判定）。</summary>
        public const string Inspect = "quality:inspect";

        /// <summary>质量数据维护（不良代码、处置决策、维修）。</summary>
        public const string Manage = "quality:manage";
    }

    /// <summary>设备与 Andon 权限。</summary>
    public static class Equipment
    {
        /// <summary>查看设备台账、状态、停机分析与 Andon 呼叫。</summary>
        public const string Read = "equipment:read";

        /// <summary>现场操作：切换设备状态、点检保养登记、Andon 呼叫与响应。</summary>
        public const string Operate = "equipment:operate";

        /// <summary>设备台账维护。</summary>
        public const string Manage = "equipment:manage";
    }

    /// <summary>用户管理权限。</summary>
    public static class Users
    {
        public const string Read = "users:read";
        public const string Manage = "users:manage";
    }

    /// <summary>角色管理权限。</summary>
    public static class Roles
    {
        public const string Read = "roles:read";
        public const string Manage = "roles:manage";
    }

    /// <summary>
    /// 全部权限（由常量反射收集，避免手工维护遗漏）。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = typeof(Permissions)
        .GetNestedTypes(BindingFlags.Public)
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(v => v, StringComparer.Ordinal)
        .ToList();

    /// <summary>判断给定权限是否在权限目录中登记。</summary>
    public static bool IsDefined(string permission)
        => All.Contains(permission, StringComparer.Ordinal);
}
