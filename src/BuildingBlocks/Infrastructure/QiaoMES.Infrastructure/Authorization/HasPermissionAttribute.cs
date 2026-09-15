using Microsoft.AspNetCore.Authorization;

namespace QiaoMES.Infrastructure.Authorization;

/// <summary>
/// 声明式权限授权：<c>[HasPermission(Permissions.WorkOrders.Create)]</c>。
/// <para>
/// 策略名约定 <c>perm:&lt;权限&gt;</c>，由 <see cref="PermissionPolicyProvider"/> 在运行时动态生成策略，
/// 无需为每个权限手工注册 Policy。
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        Policy = PolicyPrefix + permission;
        Permission = permission;
    }

    /// <summary>要求的权限标识。</summary>
    public string Permission { get; }
}
