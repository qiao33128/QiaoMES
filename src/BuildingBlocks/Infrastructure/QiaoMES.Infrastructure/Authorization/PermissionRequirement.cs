using Microsoft.AspNetCore.Authorization;

namespace QiaoMES.Infrastructure.Authorization;

/// <summary>要求当前用户具备指定权限。</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}
