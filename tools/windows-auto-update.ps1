<#
.SYNOPSIS
    本机（Windows + Docker Desktop）用镜像仓里的最新镜像启动 / 更新 QiaoMES。

.DESCRIPTION
    为什么需要它：编排文件里的 `restart: unless-stopped` 只能让容器**用现有镜像**重新起来，
    它**不会**去镜像仓拉新镜像 —— 所以「重启后自动更新」必须有一个脚本做 pull + 重建。
    本脚本就是那一步，并且设计成**幂等**（可反复执行，不会碰数据卷）：

      1. 等 Docker 引擎就绪（开机时 Docker Desktop 还在慢慢启动，这是最容易失败的一环）
      2. 就位**本机专用**的 .env（见下）
      3. 确保 external 网关网络存在（缺了 compose 会直接拒绝启动）
      4. 需要时登录镜像仓（有凭证就登录，否则检查是否已登录过）
      5. docker compose pull api web → up -d --force-recreate --wait
      6. 健康检查（容器 IP 探 /health/ready + 宿主机端口探前端）

    🔴 关于 .env（本机与服务器不一样，别混）：
      · 本机跑的是**部署版**（docker-compose.deploy.yml，拉镜像），默认端口 **8080**；
        仓库根的 `.env.example` 是**给服务器写的**（WEB_PORT=8090、CORS 指向域名），
        所以本脚本**不复制 .env.example**，而是生成一份本机专用的小 .env。
      · `POSTGRES_PASSWORD` 一律**不写**（留空 → 用编排文件默认值 qiaomes_dev）：
        本机库就是用它初始化的，凭空换密码必然连不上。
      · `JWT_SECRET_KEY` 只在**为空**时生成；如果已有值（哪怕是占位值）就**原样保留** ——
        换密钥会让所有人重新登录，而且问数页已保存的模型密钥会解不开，这不该由「启动脚本」擅自决定。
        若 .env 是从零建的，会**优先沿用当前正在运行的 api 容器的密钥**，保证这次启动不改变线上行为。
      · 需要换密钥时，请自己改 .env 后重跑（脚本会提示该怎么做）。

.PARAMETER RepoDir
    仓库根目录，默认取脚本所在目录的上一级。

.PARAMETER WaitDockerSeconds
    等 Docker 引擎就绪的最长秒数，默认 300。

.PARAMETER WaitHealthySeconds
    等健康检查的最长秒数，默认 180。设为 0 表示不等待、也不做健康检查。

.PARAMETER Registry / RegistryUser / RegistryPassword
    镜像仓与凭证。给了用户名+密码就 `docker login`（密码走 stdin，不进命令历史）；
    没给就看 `~/.docker/config.json` 里有没有登录记录，没有则只警告不中断。

.PARAMETER Open
    启动成功后用默认浏览器打开前端页面。计划任务调用时**不要**加（否则每次登录都弹浏览器）。

.PARAMETER RegisterTask
    注册「登录时自动执行」的计划任务（延迟 2 分钟）。
    ⚠️ 必须用「登录时」而不是「系统启动时」：Docker Desktop 是用户级程序，开机时引擎还没起来。
    另外请确认 Docker Desktop 已勾选 Settings → General → Start Docker Desktop when you sign in to your computer。

.PARAMETER UnregisterTask
    删除上面那个计划任务（脚本里唯一的系统级改动就是它，删掉即完全恢复）。

.PARAMETER Status
    查看计划任务的注册状态与上次执行结果。

.PARAMETER DryRun
    只打印将要执行/写入的内容，不调用 docker、不改任何文件。

.EXAMPLE
    pwsh tools/windows-auto-update.ps1 -DryRun        # 先看要做什么
    pwsh tools/windows-auto-update.ps1                # 手动执行一次
    pwsh tools/windows-auto-update.ps1 -RegisterTask  # 注册成登录时自动执行
    pwsh tools/windows-auto-update.ps1 -Status
    pwsh tools/windows-auto-update.ps1 -UnregisterTask

.NOTES
    一键启动请用 tools/start-local.ps1（= 本脚本 + -Open，名字更好找）。
#>
[CmdletBinding()]
param(
    [string]$RepoDir = (Split-Path -Parent $PSScriptRoot),
    [string]$ComposeFile = 'docker-compose.deploy.yml',
    [int]$WaitDockerSeconds = 300,
    [int]$WaitHealthySeconds = 180,
    [string]$Registry = 'ccr.ccs.tencentyun.com',
    [string]$RegistryUser,
    [string]$RegistryPassword,
    [string]$LogFile = (Join-Path $env:LOCALAPPDATA 'QiaoMES\auto-update.log'),
    [switch]$Open,
    [switch]$RegisterTask,
    [switch]$UnregisterTask,
    [switch]$Status,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$TaskName = 'QiaoMES 本机自动更新'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Log {
    param([string]$Message, [ValidateSet('INFO', 'WARN', 'ERROR', 'OK')][string]$Level = 'INFO')
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    $color = switch ($Level) { 'ERROR' { 'Red' } 'WARN' { 'Yellow' } 'OK' { 'Green' } default { 'Gray' } }
    Write-Host $line -ForegroundColor $color
    # 用 .NET 写文件：PS 5.1 的 Add-Content/Set-Content 编码行为不一致，.NET 是确定的
    try {
        $dir = Split-Path -Parent $LogFile
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        [System.IO.File]::AppendAllText($LogFile, $line + [Environment]::NewLine, $Utf8NoBom)
    }
    catch { }
}

# ---------------------------------------------------------------- .env 工具

function Get-EnvLines {
    param([string]$Path)
    # 前面的逗号强制返回数组：PowerShell 会把单元素数组"展开"成字符串，后面 .Count 就会骗人
    if (-not (Test-Path $Path)) { return , @() }
    return , @([System.IO.File]::ReadAllLines($Path))
}

function Get-EnvValue {
    param([string[]]$Lines, [string]$Key, [string]$Default = '')
    foreach ($line in $Lines) {
        if ($line -match ('^\s*' + [regex]::Escape($Key) + '=(.*)$')) { return $Matches[1].Trim() }
    }
    return $Default
}

function Set-EnvValue {
    param([string]$Path, [string]$Key, [string]$Value, [string[]]$Lines)
    $kept = @($Lines | Where-Object { $_ -notmatch ('^\s*' + [regex]::Escape($Key) + '=') })
    $new = @($kept) + @("$Key=$Value")
    if ($DryRun) { Write-Log "DRY-RUN: 会把 $Key 写入 $Path"; return }
    [System.IO.File]::WriteAllLines($Path, [string[]]$new, $Utf8NoBom)
    Write-Log "已写入 $Key（$Path）" 'OK'
}

function New-RandomHex {
    param([int]$Bytes = 32)
    $buffer = New-Object byte[] $Bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return (($buffer | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-RunningApiEnv {
    # 读运行中容器里某个环境变量的**当前生效值**（容器不存在时返回空串）
    param([string]$EnvKey, [string]$ContainerName = 'qiaomes-api')
    if ($DryRun) { return '' }
    $raw = & docker inspect $ContainerName --format '{{range .Config.Env}}{{println .}}{{end}}' 2>$null
    foreach ($line in $raw) {
        if ($line -and $line.StartsWith("$EnvKey=")) { return $line.Substring($EnvKey.Length + 1).Trim() }
    }
    return ''
}

function Get-HttpStatus {
    # 返回 HTTP 状态码；连不上 / 超时返回 0（不抛异常，方便轮询）
    param([string]$Url, [int]$TimeoutSec = 3)
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec
        return [int]$response.StatusCode
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp -and $resp.StatusCode) { return [int]$resp.StatusCode }
        return 0
    }
}

function Test-HealthEndpoint {
    # 只有「真的健康检查响应」才算通过：状态 200 **且响应不是 HTML**。
    # 🔴 为什么要校验内容类型（实测踩过）：nginx 的 `location /` 会把未知路径回退到 index.html，
    #    所以 /health/ready 在**没有专门代理它的旧镜像**上同样返回 200 —— 内容是 SPA 的 HTML。
    #    只看状态码就会把「前端在跑」误判成「API 已就绪」，健康检查直接变成永远通过的摆设。
    param([string]$Url, [int]$TimeoutSec = 3)
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec $TimeoutSec
        if ([int]$response.StatusCode -ne 200) { return $false }
        $contentType = [string]($response.Headers['Content-Type'] -join ';')
        if ($contentType -match 'text/html') { return $false }
        return $true
    }
    catch { return $false }
}

function Test-ApiReady {
    # 🔴 平台差异（实测踩过）：**容器 IP 只有 Linux 宿主机能直连**。
    #    Docker Desktop（Windows 的 WSL2 后端）里那个 172.x 网段在虚拟机内部，
    #    Windows 宿主路由不到 → 探容器 IP 一定超时（不是服务没起来！）。
    #    部署脚本 gh_deploy_aliyun.py 用的是容器 IP，因为服务器是 Linux，那边没问题。
    param([string]$Ip, [int]$WebPort)
    if ($Ip -and (Test-HealthEndpoint "http://${Ip}:8080/health/ready" 2)) { return $true }
    if (Test-HealthEndpoint "http://127.0.0.1:${WebPort}/health/ready" 3) { return $true }

    # 到这一步说明拿不到「真的健康响应」：可能是这个版本的 web 镜像里 nginx 还没代理 /health。
    # 那就退回「必然返回 401 的鉴权接口」——能拿到 401 就证明 API 进程活着（比 /health 弱一档，但好过瞎判）。
    $alive = Get-HttpStatus "http://127.0.0.1:${WebPort}/api/auth/me" 3
    if ($alive -eq 401 -or $alive -eq 403) { return $true }

    return $false
}

function Invoke-Docker {
    param([string[]]$DockerArgs)
    $cmd = 'docker ' + ($DockerArgs -join ' ')
    if ($DryRun) { Write-Log "DRY-RUN: $cmd"; return @() }
    Write-Log $cmd   # 真实执行也记一行：日志里能看清每次到底跑了什么
    $output = & docker @DockerArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ("docker " + ($DockerArgs -join ' ') + " 失败（exit $LASTEXITCODE）：`n" + ($output -join [Environment]::NewLine))
    }
    return $output
}

# ---------------------------------------------------------------- 计划任务

function Show-TaskStatus {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) { Write-Host "未注册计划任务「$TaskName」。" -ForegroundColor Yellow; return }
    $info = Get-ScheduledTaskInfo -TaskName $TaskName
    Write-Host "任务名称：$TaskName"
    Write-Host "触发器： $($task.Triggers | ForEach-Object { $_.CimClass.CimClassName })"
    Write-Host "状态：   $($task.State)"
    Write-Host "上次运行：$($info.LastRunTime)（结果码 $($info.LastTaskResult)）"
    Write-Host "下次运行：$($info.NextRunTime)"
}

function Register-AutoUpdateTask {
    $scriptPath = $PSCommandPath
    $hostExe = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not $hostExe) { $hostExe = (Get-Command powershell -ErrorAction Stop).Source }

    $argument = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -RepoDir "{1}"' -f $scriptPath, $RepoDir
    $action = New-ScheduledTaskAction -Execute $hostExe -Argument $argument
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    # 登录触发器支持延迟（Win10+）；不支持就退化为「脚本内部等引擎」，功能不受影响
    try { $trigger.Delay = 'PT2M' } catch { Write-Log '当前系统不支持给登录触发器加延迟，将由脚本内部等待引擎' 'WARN' }

    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
        -Description 'QiaoMES：登录后拉取镜像仓最新镜像并重建本机容器（幂等，不碰数据卷）。由 tools/windows-auto-update.ps1 -RegisterTask 注册。' `
        -Force | Out-Null

    Write-Log "已注册计划任务「$TaskName」：$hostExe" 'OK'
    Write-Log "登录后延迟 2 分钟执行：$scriptPath -RepoDir $RepoDir"
    Write-Log '⚠️ 还需确认 Docker Desktop 已勾选 Settings → General → Start Docker Desktop when you sign in to your computer'
    Write-Log '查看/删除：pwsh tools/windows-auto-update.ps1 -Status / -UnregisterTask'
}

# ---------------------------------------------------------------- 主流程

if ($RegisterTask) {
    try { Register-AutoUpdateTask } catch { Write-Log $_.Exception.Message 'ERROR'; exit 1 }
    exit 0
}
if ($UnregisterTask) {
    try {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction Stop
        Write-Log "已删除计划任务「$TaskName」" 'OK'
    }
    catch { Write-Log $_.Exception.Message 'ERROR'; exit 1 }
    exit 0
}
if ($Status) { Show-TaskStatus; exit 0 }

try {
    Write-Log "===== 开始（仓库 $RepoDir）====="
    if ($DryRun) { Write-Log 'DRY-RUN 模式：只打印，不调用 docker、不写文件' 'WARN' }

    # ---------- 0. 前置检查 ----------
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw '找不到 docker 命令 —— 请确认已安装 Docker Desktop 并至少启动过一次。'
    }
    $composePath = Join-Path $RepoDir $ComposeFile
    if (-not (Test-Path $composePath)) { throw "找不到编排文件：$composePath（用 -RepoDir 指定仓库根目录）" }

    # ---------- 1. 等 Docker 引擎 ----------
    $deadline = (Get-Date).AddSeconds($WaitDockerSeconds)
    $engineReady = $false
    $attempt = 0
    while ($true) {
        if ($DryRun) { Write-Log 'DRY-RUN: 跳过引擎等待'; $engineReady = $true; break }
        $attempt++
        $null = & docker info 2>&1
        if ($LASTEXITCODE -eq 0) { $engineReady = $true; break }
        if ((Get-Date) -ge $deadline) { break }
        if ($attempt % 6 -eq 1) {
            Write-Log '引擎还没就绪（开机时 Docker Desktop 需要一会儿），继续等…' 'WARN'
        }
        Start-Sleep -Seconds 5
    }
    if (-not $engineReady) {
        throw ("等待 Docker 引擎超过 $WaitDockerSeconds 秒仍未就绪。请确认 Docker Desktop 已启动，并勾选 " +
               'Settings → General → Start Docker Desktop when you sign in to your computer。')
    }
    Write-Log 'Docker 引擎就绪' 'OK'

    # ---------- 2. .env（本机专用；只补必要项，绝不改已有值）----------
    $envPath = Join-Path $RepoDir '.env'
    $lines = Get-EnvLines $envPath
    if ($lines.Count -eq 0) {
        # 🔴 刻意**不复制 .env.example**：那是给服务器写的（8090 端口、域名 CORS），
        # 复制过来会让本机端口从 8080 变成 8090 —— 一个「启动脚本顺手改了端口」的经典坑。
        # JWT 优先沿用当前运行的 api 容器，保证这次启动不改变任何既有行为（否则所有人要重新登录、
        # 问数页已保存的模型密钥也会解不开）。
        $existingJwt = Get-RunningApiEnv 'Jwt__SecretKey'
        $newJwt = ''
        if ($existingJwt) {
            $newJwt = $existingJwt
            Write-Log '新建 .env：JWT_SECRET_KEY 沿用当前运行中的 api 容器，行为不变'
        }
        else {
            $newJwt = New-RandomHex 32
            Write-Log '新建 .env：没有运行中的 api 容器，已生成随机 JWT_SECRET_KEY'
        }
        $template = @(
            '# 由 tools/windows-auto-update.ps1 生成 —— 本机运行用（默认端口 8080）',
            '# 服务器请改用仓库根的 .env.example（WEB_PORT=8090、CORS 指向域名），两者别混用。',
            '# 本文件在 .gitignore 里，不会进仓库。',
            '',
            'WEB_PORT=8080',
            'CORS_ORIGINS=http://localhost:8080',
            'GATEWAY_NETWORK=kanban_net',
            '# 留空 = 用编排文件默认值 qiaomes_dev。本机库就是用它初始化的，别在这里换密码。',
            'POSTGRES_PASSWORD=',
            '# 🔴 Docker Desktop 只代理 0.0.0.0 的端口映射，不写这行 Windows 宿主就连不上数据库',
            '#（本机是开发机、不是公网服务器，所以这里可以放开；服务器保持默认的 127.0.0.1）。',
            'POSTGRES_BIND=0.0.0.0',
            '# 想一键灌演示数据就改成 true，然后跑 pwsh tools/seed-demo.ps1',
            'DEMO_DATA_ENABLED=false',
            "JWT_SECRET_KEY=$newJwt"
        )
        if ($DryRun) { Write-Log "DRY-RUN: 会新建 $envPath" }
        else {
            [System.IO.File]::WriteAllLines($envPath, [string[]]$template, $Utf8NoBom)
            Write-Log "已新建 $envPath（本机专用：WEB_PORT=8080）" 'OK'
        }
        $lines = Get-EnvLines $envPath
    }

    $jwt = Get-EnvValue $lines 'JWT_SECRET_KEY'
    if ([string]::IsNullOrWhiteSpace($jwt)) {
        # 空的必须补：docker-compose.deploy.yml 把 Jwt__SecretKey 声明成必填（${JWT_SECRET_KEY:?}），
        # 空值会让 compose 直接拒绝启动。
        Set-EnvValue -Path $envPath -Key 'JWT_SECRET_KEY' -Value (New-RandomHex 32) -Lines $lines
        $lines = Get-EnvLines $envPath
        $jwt = Get-EnvValue $lines 'JWT_SECRET_KEY'
    }
    elseif ($jwt -match 'Change_Me') {
        Write-Log 'JWT_SECRET_KEY 还是模板里的占位值（等于没有密钥）。本次**不动它**（换掉会让所有人重新登录、问数页已存的模型密钥失效）；' 'WARN'
        Write-Log '确实要换：把 .env 里的 JWT_SECRET_KEY 改成一个 ≥32 字符的随机串，再重跑本脚本。' 'WARN'
    }

    # 自迭代服务（容器 ai-iteration）与 QiaoMES 共用这个 AdminKey：它空着时，迭代服务会拒绝一切管理员操作（503），
    # 「改进建议」页就会报 401/403。这个值只在本机这两个容器之间用，生成随机串即可（与本机之外的那份互不相干）。
    if (-not (Get-EnvValue $lines 'ITERATION_ADMIN_KEY')) {
        Set-EnvValue -Path $envPath -Key 'ITERATION_ADMIN_KEY' -Value (New-RandomHex 16) -Lines $lines
        $lines = Get-EnvLines $envPath
        Write-Log '已生成 ITERATION_ADMIN_KEY（QiaoMES 与自迭代服务共用同一个值）'
    }

    # 地址就填服务名 —— 两者在同一个 compose 网络里，容器名可直接解析。
    if (-not (Get-EnvValue $lines 'ITERATION_BASE_URL')) {
        Set-EnvValue -Path $envPath -Key 'ITERATION_BASE_URL' -Value 'http://ai-iteration:8080' -Lines $lines
        $lines = Get-EnvLines $envPath
        Write-Log '已填 ITERATION_BASE_URL=http://ai-iteration:8080（本机一键启动会把自迭代服务一起带起来）'
    }

    # 🔴 本机必须把数据库端口绑到 0.0.0.0（见文件头的平台说明）：Docker Desktop 不代理 127.0.0.1 的映射，
    # 否则从 Windows 宿主 `dotnet run` / 数据库工具连 localhost:5432 会直接被拒。
    # 幂等：已有值就不动；老的 .env（这次改动之前建的）缺这一项，这里自动补上。
    if (-not (Get-EnvValue $lines 'POSTGRES_BIND')) {
        Set-EnvValue -Path $envPath -Key 'POSTGRES_BIND' -Value '0.0.0.0' -Lines $lines
        $lines = Get-EnvLines $envPath
        Write-Log '已补上 POSTGRES_BIND=0.0.0.0（本机需要宿主直连数据库；服务器那份默认 127.0.0.1）'
    }

    if (-not (Get-EnvValue $lines 'POSTGRES_PASSWORD')) {
        Write-Log 'POSTGRES_PASSWORD 留空 → 沿用编排文件默认值 qiaomes_dev（本机库就是这么初始化的，这里刻意不生成新密码）'
    }

    $webPort = Get-EnvValue $lines 'WEB_PORT' '8080'

    # ---------- 3. 配置目录 + external 网关网络 ----------
    # 「改进建议 → 迭代服务配置」保存的文件落在这个目录（编排把它挂到容器的 /app/config）。
    # 提前建好：让 Docker 自动创建时属主是 root，容器里的非 root 用户可能写不进去。
    $configDir = Join-Path $RepoDir 'config'
    if (-not (Test-Path $configDir)) {
        if ($DryRun) { Write-Log "DRY-RUN: 会创建 $configDir" }
        else {
            New-Item -ItemType Directory -Path $configDir -Force | Out-Null
            Write-Log "已创建 $configDir（迭代服务配置落盘在这里；已在 .gitignore 里排除）" 'OK'
        }
    }

    # 自迭代服务的 SQLite 目录。Docker 也会自动建，但那样属主是 root —— 容器里的非 root 用户可能写不进去，
    # 所以这里用你的身份先建好（和上面 config 同一个道理）。
    $iterDataDir = Join-Path $RepoDir 'ai-iteration\data'
    if (-not (Test-Path $iterDataDir)) {
        if ($DryRun) { Write-Log "DRY-RUN: 会创建 $iterDataDir" }
        else {
            New-Item -ItemType Directory -Path $iterDataDir -Force | Out-Null
            Write-Log "已创建 $iterDataDir（自迭代服务的 SQLite 落盘在这里）" 'OK'
        }
    }

    $network = Get-EnvValue $lines 'GATEWAY_NETWORK' 'kanban_net'
    if ($DryRun) {
        Write-Log "DRY-RUN: 会检查/创建网络 $network"
    }
    else {
        $null = & docker network inspect $network 2>&1
        if ($LASTEXITCODE -ne 0) {
            $null = Invoke-Docker @('network', 'create', $network)
            Write-Log "已创建网络 $network（编排文件把它声明为 external，缺了会直接拒绝启动）" 'OK'
        }
        else { Write-Log "网络 $network 已存在" }
    }

    # ---------- 4. 镜像仓登录（能跳过就跳过）----------
    if ($RegistryUser -and $RegistryPassword) {
        if ($DryRun) { Write-Log "DRY-RUN: docker login $Registry -u $RegistryUser" }
        else {
            Write-Log "docker login $Registry -u $RegistryUser（密码走 stdin）"
            $RegistryPassword | & docker login $Registry -u $RegistryUser --password-stdin 2>&1 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "docker login $Registry 失败（用户名/密码错？TCR 个人版要用「访问凭证」里的密码）" }
            Write-Log '已登录镜像仓' 'OK'
        }
    }
    else {
        $dockerConfig = Join-Path $HOME '.docker\config.json'
        $loggedIn = (Test-Path $dockerConfig) -and (Select-String -Path $dockerConfig -Pattern ([regex]::Escape($Registry)) -Quiet)
        if ($loggedIn) { Write-Log "已登录过 $Registry，跳过登录" }
        else {
            Write-Log "没有 $Registry 的登录记录。若是私有镜像仓，下一步 pull 会失败 —— 加上 -RegistryUser/-RegistryPassword 再跑一次。" 'WARN'
        }
    }

    # ---------- 5. pull + up ----------
    $composeArgs = @('compose', '-f', $ComposeFile, '--env-file', '.env')
    $supportsWait = $false
    if (-not $DryRun -and $WaitHealthySeconds -gt 0) {
        $help = & docker compose up --help 2>&1 | Out-String
        $supportsWait = $help -match '--wait-timeout'
    }

    Push-Location $RepoDir
    try {
        $null = Invoke-Docker ($composeArgs + @('pull', 'api', 'web'))

        $upArgs = $composeArgs + @('up', '-d', '--force-recreate')
        if ($supportsWait) { $upArgs += @('--wait', '--wait-timeout', "$WaitHealthySeconds") }
        $upArgs += @('api', 'web')
        $null = Invoke-Docker $upArgs
    }
    finally { Pop-Location }

    # ---------- 6. 健康检查 ----------
    if ($DryRun) { Write-Log 'DRY-RUN: 会探 /health/ready 与前端端口' }

    if (-not $DryRun -and $WaitHealthySeconds -gt 0) {
        # 🔴 用「墙钟预算」而不是「轮数 × 每轮耗时」：后者会因为每轮请求都要等满超时而远超预期
        #    （实测过：10s/轮 × 60 轮 = 8 分钟，用户看到的是「卡住」）。
        $apiDeadline = (Get-Date).AddSeconds($WaitHealthySeconds)
        $apiOk = $false
        while ((Get-Date) -lt $apiDeadline) {
            $raw = & docker inspect qiaomes-api --format '{{range .NetworkSettings.Networks}}{{println .IPAddress}}{{end}}' 2>$null
            $ip = @($raw | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim() })[0]
            if (Test-ApiReady -Ip $ip -WebPort $webPort) { $apiOk = $true; break }
            Start-Sleep -Seconds 2
        }
        if (-not $apiOk) {
            $tail = & docker logs --tail 40 qiaomes-api 2>&1 | Out-String
            throw ("API 未就绪（探了 $WaitHealthySeconds 秒）。常见原因：JWT_SECRET_KEY 缺失、POSTGRES_PASSWORD 与数据卷不一致。" +
                   [Environment]::NewLine + '--- qiaomes-api 日志尾部 ---' + [Environment]::NewLine + $tail)
        }
        Write-Log 'API 就绪探测通过' 'OK'

        $webDeadline = (Get-Date).AddSeconds(30)   # nginx 起得很快，给 30 秒够了
        $webOk = $false
        while ((Get-Date) -lt $webDeadline) {
            if ((Get-HttpStatus "http://127.0.0.1:$webPort/" 3) -eq 200) { $webOk = $true; break }
            Start-Sleep -Seconds 2
        }
        if (-not $webOk) {
            $tail = & docker logs --tail 30 qiaomes-web 2>&1 | Out-String
            throw ("前端端口 $webPort 探不通。" +
                   [Environment]::NewLine + '--- qiaomes-web 日志尾部 ---' + [Environment]::NewLine + $tail)
        }
        Write-Log "前端 http://localhost:$webPort/ 通过" 'OK'
    }

    # ---------- 7. 打开浏览器（仅一键启动时）----------
    $url = "http://localhost:$webPort"
    if ($Open) {
        Write-Log "打开浏览器：$url"
        if (-not $DryRun) { Start-Process $url }
    }

    Write-Log "===== 完成：$url =====" 'OK'
    exit 0
}
catch {
    Write-Log $_.Exception.Message 'ERROR'
    Write-Log "详细日志：$LogFile" 'ERROR'
    exit 1
}
