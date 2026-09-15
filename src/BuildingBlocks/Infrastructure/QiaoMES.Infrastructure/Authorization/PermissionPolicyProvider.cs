using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace QiaoMES.Infrastructure.Authorization;

/// <summary>
/// 动态策略提供者：遇到 <c>perm:xxx</c> 形式的策略名时按需构造策略，免去逐个注册 Policy。
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null)
        {
            return existing;
        }

        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}
