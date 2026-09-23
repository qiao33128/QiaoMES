<#
.SYNOPSIS
    本机起 **开发环境**（候选版本，端口 8092）—— 拉 `:dev` 镜像、独立库、独立密钥。

.DESCRIPTION
    分支 = 环境（见 docs/BRANCHING.md）：
      · `main` → 生产（服务器）：`tools/start-local.ps1`（或部署到服务器）
      · `dev`  → 开发环境（**你这台机器**）：本脚本
    为什么开发环境跑在本机而不是服务器：那台机器只有 2 vCPU / 896MB，多一套栈会把磁盘 I/O 拖死、
    连生产一起不可达（2026-09-23 实际发生过一次）。本机内存和磁盘都富余得多。

    它做的事（幂等，可反复跑）：
      1. 等 Docker 引擎就绪
      2. 就位 `.env.dev`：镜像指到 `:dev` 标签，并**在本机生成**独立的 JWT 密钥与数据库密码
         （刻意不与生产共用 —— 共用 JWT 等于开发环境签发的令牌在生产有效）
      3. `docker compose -f docker-compose.dev.yml --env-file .env.dev pull api web`
      4. `up -d --force-recreate api web`（compose 项目名 = qiaomes-dev，容器 qiaomes-dev-*）
      5. 健康检查（API 就绪 + 前端端口），成功后可打开浏览器

    与生产的隔离：独立 compose 项目 / 容器名 / 数据卷 / 网络 / 端口 / 密钥，互不影响。
    首次起来会在空库上跑全量迁移，比平时慢（脚本等 4 分钟）。

.PARAMETER DevPort
    本机对外端口，默认 8092（生产那一套默认 8080，两者可同时跑）。

.PARAMETER NoPull
    跳过 `docker compose pull`（本地刚构建过镜像时用）。

.PARAMETER Open
    起来后打开浏览器（默认开）。

.PARAMETER Tag
    要跑的镜像标签，默认 `dev`（CI 每次 push dev 分支都会更新它）。
    填某个 git SHA 可以看历史版本，例如 -Tag abc1234。

.EXAMPLE
    pwsh tools/start-dev.ps1
    pwsh tools/start-dev.ps1 -NoPull -Open
    pwsh tools/start-dev.ps1 -Tag 92027ad
#>
[CmdletBinding()]
param(
    [string]$RepoDir = (Split-Path -Parent $PSScriptRoot),
    [int]$DevPort = 8092,
    [string]$Tag = 'dev',
    [string]$Registry = 'ccr.ccs.tencentyun.com',
    [string]$RegistryUser,
    [string]$RegistryPassword,
    [switch]$NoPull,
    [switch]$Open,
    [int]$WaitDockerSeconds = 300,
    [int]$WaitHealthySeconds = 240
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $color = switch ($Level) {
        'ERROR' { 'Red' }
        'WARN'  { 'Yellow' }
        'OK'    { 'Green' }
        default { 'Gray' }
    }
    Write-Host ("[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $Message) -ForegroundColor $color
}

function New-RandomHex {
    param([int]$Bytes = 32)
    $buffer = New-Object byte[] $Bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    return -join ($buffer | ForEach-Object { $_.ToString('x2') })
}

function Get-EnvFileLines {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return , @() }
    return , @([System.IO.File]::ReadAllLines($Path))
}

function Get-EnvValue {
    param([string[]]$Lines, [string]$Key, [string]$Default = '')
    foreach ($line in $Lines) {
        if ($line -like "$Key=*") { return $line.Substring($Key.Length + 1).Trim() }
    }
    return $Default
}

function Set-EnvValue {
    param([string]$Path, [string]$Key, [string]$Value, [string[]]$Lines)
    $kept = @($Lines | Where-Object { -not ($_ -like "$Key=*") })
    $kept += "$Key=$Value"
    [System.IO.File]::WriteAllLines($Path, [string[]]$kept, $Utf8NoBom)
}

# ---------- 0. 前置 ----------
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw '找不到 docker 命令 —— 请确认已安装 Docker Desktop 并至少启动过一次。'
}

$composePath = Join-Path $RepoDir 'docker-compose.dev.yml'
if (-not (Test-Path $composePath)) { throw "找不到开发环境编排文件：$composePath" }

$envFile = Join-Path $RepoDir '.env.dev'
$composeName = 'docker-compose.dev.yml'

# ---------- 1. 等 Docker 引擎 ----------
$deadline = (Get-Date).AddSeconds($WaitDockerSeconds)
$ready = $false
while ((Get-Date) -lt $deadline) {
    & docker info 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Write-Log '等 Docker 引擎就绪…（Docker Desktop 刚启动时引擎要几十秒）'
    Start-Sleep -Seconds 5
}
if (-not $ready) { throw "Docker 引擎 ${WaitDockerSeconds} 秒内没就绪 —— 请先打开 Docker Desktop。" }
Write-Log 'Docker 引擎就绪' 'OK'

# ---------- 2. .env.dev ----------
$lines = Get-EnvFileLines $envFile
if ($lines.Count -eq 0) {
    Write-Log "没有 $envFile，新建一份（本机开发环境专用，已在 .gitignore 里）"
    $template = @(
        '# 由 tools/start-dev.ps1 生成 —— 本机**开发环境**用（端口 8092，独立库与独立密钥）',
        '# 与仓库根的 .env（生产/本机生产镜像那套）互不影响，别混用。',
        '',
        "QIAOMES_API_IMAGE=$Registry/qiaoqiao11/qiaomes-api:$Tag",
        "QIAOMES_WEB_IMAGE=$Registry/qiaoqiao11/qiaomes-web:$Tag",
        "DEV_WEB_PORT=$DevPort",
        "CORS_ORIGINS=http://localhost:$DevPort",
        'DEMO_DATA_ENABLED=true',
        '# 下面两个是**本机随机生成**、与生产不同的密钥：共用 JWT 会让开发环境签发的令牌在生产有效。',
        "JWT_SECRET_KEY=$(New-RandomHex 32)",
        "POSTGRES_PASSWORD=$(New-RandomHex 24)"
    )
    [System.IO.File]::WriteAllLines($envFile, [string[]]$template, $Utf8NoBom)
    $lines = Get-EnvFileLines $envFile
    Write-Log "已新建 $envFile" 'OK'
}
else {
    # 幂等：缺哪个补哪个，已有的一概不动（尤其两个密钥 —— 换了就连不上已初始化的开发库）
    $image = Get-EnvValue $lines "QIAOMES_API_IMAGE"
    $wantImage = "$Registry/qiaoqiao11/qiaomes-api:$Tag"
    if ($image -ne $wantImage) {
        Set-EnvValue -Path $envFile -Key 'QIAOMES_API_IMAGE' -Value $wantImage -Lines $lines
        Set-EnvValue -Path $envFile -Key 'QIAOMES_WEB_IMAGE' -Value "$Registry/qiaoqiao11/qiaomes-web:$Tag" -Lines $lines
        $lines = Get-EnvFileLines $envFile
        Write-Log "镜像标签已对齐到 :$Tag"
    }
    if (-not (Get-EnvValue $lines 'JWT_SECRET_KEY')) {
        Set-EnvValue -Path $envFile -Key 'JWT_SECRET_KEY' -Value (New-RandomHex 32) -Lines $lines
        $lines = Get-EnvFileLines $envFile
        Write-Log '补上开发环境专用 JWT_SECRET_KEY'
    }
    if (-not (Get-EnvValue $lines 'POSTGRES_PASSWORD')) {
        Set-EnvValue -Path $envFile -Key 'POSTGRES_PASSWORD' -Value (New-RandomHex 24) -Lines $lines
        $lines = Get-EnvFileLines $envFile
        Write-Log '补上开发库密码（只在开发卷首次初始化时生效）'
    }
    if (-not (Get-EnvValue $lines 'DEV_WEB_PORT')) {
        Set-EnvValue -Path $envFile -Key 'DEV_WEB_PORT' -Value "$DevPort" -Lines $lines
        $lines = Get-EnvFileLines $envFile
    }
}

# ---------- 3. 登录镜像仓（需要时） ----------
if ($RegistryUser -and $RegistryPassword) {
    $RegistryPassword | & docker login $Registry -u $RegistryUser --password-stdin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'docker login 失败（TCR 个人版要用「访问凭证」里的密码，不是登录密码）' }
    Write-Log '已登录镜像仓' 'OK'
}
else {
    $dockerConfig = Join-Path $HOME '.docker\config.json'
    $loggedIn = (Test-Path $dockerConfig) -and (Select-String -Path $dockerConfig -Pattern ([regex]::Escape($Registry)) -Quiet)
    if ($loggedIn) { Write-Log "已登录过 $Registry，跳过登录" }
    else { Write-Log "没有 $Registry 的登录记录；私有镜像仓下一步 pull 会失败 —— 加 -RegistryUser/-RegistryPassword 再跑" 'WARN' }
}

# ---------- 4. pull + up ----------
$composeArgs = @('compose', '-f', $composeName, '--env-file', '.env.dev')
Push-Location $RepoDir
try {
    if (-not $NoPull) {
        Write-Log "拉取 :$Tag 镜像（首次会下载几百 MB）"
        & docker @composeArgs pull api web
        if ($LASTEXITCODE -ne 0) { throw 'docker compose pull 失败（看上面的报错；多为镜像仓未登录或 tag 不存在）' }
    }
    else { Write-Log '-NoPull：跳过拉取，直接用本地已有镜像' }

    Write-Log '启动开发环境（独立 compose 项目 qiaomes-dev）'
    & docker @composeArgs up -d --force-recreate api web
    if ($LASTEXITCODE -ne 0) { throw 'docker compose up 失败（端口 8092 被占？看上面的报错）' }
}
finally {
    Pop-Location
}

# ---------- 5. 健康检查 ----------
if ($WaitHealthySeconds -gt 0) {
    Write-Log "等健康检查（首次要在空库上跑全量迁移，最多 $WaitHealthySeconds 秒）"
    # 🔴 必须走**发布端口**（8092 → web 的 nginx → 反代到 api 的 /health/），**不要探容器 IP**：
    # Docker Desktop（Windows）的容器 IP 在 WSL 虚拟机里，Windows 宿主路由不到 —— 探它必然超时，
    # 于是把"其实已经起好了"判成失败（这条坑 MEMORY 里记着）。nginx.conf 里那句
    # `location /health/ { proxy_pass http://api:8080; }` 正是为这种探活准备的。
    $deadline = (Get-Date).AddSeconds($WaitHealthySeconds)
    $ok = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest "http://127.0.0.1:$DevPort/health/ready" -UseBasicParsing -TimeoutSec 6
            if ($r.StatusCode -eq 200) { $ok = $true; break }
        }
        catch { }
        Start-Sleep -Seconds 6
    }
    if (-not $ok) {
        Write-Log '开发环境 API 健康检查没通过，下面是 api 日志尾部：' 'ERROR'
        & docker logs --tail 40 qiaomes-dev-api 2>&1 | Select-Object -Last 40
        exit 1
    }
    Write-Log '开发环境 API 健康检查通过' 'OK'

    try {
        $web = Invoke-WebRequest "http://127.0.0.1:$DevPort/" -UseBasicParsing -TimeoutSec 8
        if ($web.StatusCode -eq 200) { Write-Log "开发环境前端探活通过（http://localhost:$DevPort/）" 'OK' }
    }
    catch {
        Write-Log "前端端口 $DevPort 还没响应，看一下 qiaomes-dev-web 的日志" 'WARN'
        & docker logs --tail 20 qiaomes-dev-web 2>&1 | Select-Object -Last 20
    }
}

Write-Host ''
Write-Log "开发环境就绪：http://localhost:$DevPort/  （镜像是 :$Tag，容器 qiaomes-dev-*）" 'OK'
Write-Log '停止：docker compose -p qiaomes-dev stop ；清空开发库重来：docker compose -p qiaomes-dev down -v' 
Write-Log '注意：这个环境刻意不接自迭代服务（数据隔离），「改进建议」页会提示未配置 —— 那是正常状态。'

if ($Open) {
    Start-Process "http://localhost:$DevPort/"
}
