namespace QiaoMES.Shared.Authorization;

/// <summary>本项目自定义的 Claim 类型。</summary>
public static class QiaoMESClaimTypes
{
    /// <summary>权限声明。仅供前端渲染菜单使用，服务端鉴权始终以数据库权限为准。</summary>
    public const string Permission = "permission";
}
