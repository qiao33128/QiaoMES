using System.Text;
using System.Text.Json;

namespace QiaoMES.Api.Iteration;

/// <summary>当前**生效**的配置：配置文件优先，没有文件（或字段缺失）才回落到部署配置（appsettings / 环境变量）。</summary>
public sealed record IterationEffectiveSettings(
    string BaseUrl,
    string AdminKey,
    int TimeoutSeconds,
    /// <summary><c>file</c> = 有人在页面上配过；<c>configuration</c> = 仍在用 appsettings / 环境变量。</summary>
    string Source,
    DateTime? UpdatedAt,
    string? UpdatedBy)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}

/// <summary>
/// 迭代服务配置的落盘文件（**不进数据库**）。
/// <para>
/// 为什么不是 <c>.env</c> / 环境变量：环境变量在**进程启动后改不动**（即便进程自己改了也不持久，
/// 下次启动又回到镜像 / compose 的值），而部署每次都是 <c>--force-recreate</c>，
/// 写在容器可写层里的文件会被丢掉。所以"页面上能改 + 保存即生效 + 重启后还在"必须落在一个
/// **挂载出来的**运行时文件上（编排里把宿主目录挂到 <c>/app/config</c>）。
/// </para>
/// <para>
/// 为什么不在文件里再加密一层：它的保护级别与 <c>.env</c> 完全一致 —— 都在同一台机器的文件系统上、
/// 都只有属主（容器里的 <c>app</c> 用户 / 宿主上的 root）能读，写成 <c>0600</c>。
/// 而能读到这个文件的人，基本也能读到 <c>Jwt:SecretKey</c>（同一台机器），
/// 因此在这里加密只是"看起来更安全"，还把排查问题变复杂 —— 不值当。
/// </para>
/// <para>
/// 🔴 **不缓存**：每次读都直接读盘。文件只有几百字节，读一次是微秒级；
/// 而任何形式的缓存都会带来"手工删了/改了文件，页面还显示旧值"这种最难解释的现象。
/// </para>
/// <para>
/// 文件格式（<c>config/iteration.json</c>）：
/// <code>
/// { "BaseUrl": "http://ai-iteration:8080", "AdminKey": "……", "TimeoutSeconds": 180,
///   "UpdatedAt": "2026-09-22T06:00:00Z", "UpdatedBy": "admin" }
/// </code>
/// 其中 <c>BaseUrl</c> 为空串 = 页面上**显式关停**该功能；字段缺失（null）= 沿用部署配置。
/// </para>
/// </summary>
public sealed class IterationSettingsFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IterationOptions _deployment;
    private readonly ILogger<IterationSettingsFile> _logger;

    public IterationSettingsFile(
        IterationOptions deployment,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<IterationSettingsFile> logger)
    {
        _deployment = deployment;
        _logger = logger;

        var configured = configuration[$"{IterationOptions.SectionName}:ConfigFile"];
        FilePath = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "config", "iteration.json")
            : Path.GetFullPath(configured);

        // 启动时把"配置文件在哪、在不在"打出来 —— 出问题时第一眼就能看到路径对不对
        logger.LogInformation(
            "迭代服务：配置文件 {Path}（{State}；不存在时沿用 appsettings / 环境变量）",
            FilePath, File.Exists(FilePath) ? "存在" : "尚未创建");
    }

    /// <summary>配置文件绝对路径。</summary>
    public string FilePath { get; }

    /// <summary>读当前生效配置（每次直接读盘）。</summary>
    public IterationEffectiveSettings Read() => Effective(TryLoad());

    /// <summary>
    /// 写入配置文件（原子写：先写同目录临时文件再 <c>rename</c>，避免读到半截内容）。
    /// <para>Linux 上写成 <c>0600</c>：这个文件里有管理员密钥，不该被同机其它用户读到。</para>
    /// </summary>
    public IterationEffectiveSettings Write(string baseUrl, string adminKey, int timeoutSeconds, string actor)
    {
        var model = new IterationFileModel(baseUrl, adminKey, timeoutSeconds, DateTime.UtcNow, actor);
        var json = JsonSerializer.Serialize(model, JsonOptions) + Environment.NewLine;

        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, FilePath, overwrite: true);

        return Effective(model);
    }

    private IterationFileModel? TryLoad()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(FilePath);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize<IterationFileModel>(text, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 文件被手改坏了 / 权限不对：不阻断功能，按「没有配置文件」处理（沿用部署配置），
            // 但必须留下日志 —— 静默回退会让人以为"页面上保存过的配置丢了"。
            _logger.LogWarning(ex, "迭代服务：配置文件 {Path} 读不出来，本次按「没有配置文件」处理（沿用部署配置）", FilePath);
            return null;
        }
    }

    private IterationEffectiveSettings Effective(IterationFileModel? model)
    {
        if (model is null)
        {
            return new IterationEffectiveSettings(
                _deployment.BaseUrl, _deployment.AdminKey, _deployment.TimeoutSeconds, "configuration", null, null);
        }

        // 空串 = 页面上显式清空（关停 / 清掉密钥），必须能覆盖部署配置；null = 未指定，沿用部署配置
        return new IterationEffectiveSettings(
            model.BaseUrl ?? _deployment.BaseUrl,
            model.AdminKey ?? _deployment.AdminKey,
            model.TimeoutSeconds ?? _deployment.TimeoutSeconds,
            "file",
            model.UpdatedAt,
            model.UpdatedBy);
    }

    private sealed record IterationFileModel(
        string? BaseUrl,
        string? AdminKey,
        int? TimeoutSeconds,
        DateTime? UpdatedAt,
        string? UpdatedBy);
}
