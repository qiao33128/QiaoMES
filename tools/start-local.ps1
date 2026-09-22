<#
.SYNOPSIS
    本机 QiaoMES 一键启动：拉取镜像仓最新镜像 → 起容器 → 打开浏览器。

.DESCRIPTION
    它本身不重复实现逻辑，而是把参数原样交给 tools/windows-auto-update.ps1
    （同一套流程只维护一份：等 Docker 引擎 → 就位本机 .env → 建网关网络 → 需要时登录镜像仓
     → docker compose pull → up -d --force-recreate --wait → 健康检查）。

    与「开机自动更新」的区别只有两点：
      · 这里会**打开浏览器**（那个脚本被计划任务调用，不该弹浏览器）；
      · 这里默认等人看懂输出，适合你想「现在就跑起来」的场景。

    跑完的结果：容器用的是**镜像仓里的版本**，不再是本地构建版（两者容器名与数据卷相同，数据不受影响）。
    想回到本地构建版：docker compose up -d --build

.PARAMETER WaitHealthySeconds
    等健康检查的最长秒数，默认 180。设为 0 表示不等待。

.PARAMETER Registry / RegistryUser / RegistryPassword
    私有镜像仓凭证。首次或未登录时给一次即可（登录状态会留在 ~/.docker/config.json）。

.EXAMPLE
    pwsh tools/start-local.ps1
    pwsh tools/start-local.ps1 -RegistryUser 100047229903 -RegistryPassword xxxx
    pwsh tools/start-local.ps1 -WaitHealthySeconds 0
#>
[CmdletBinding()]
param(
    [string]$RepoDir = (Split-Path -Parent $PSScriptRoot),
    [int]$WaitHealthySeconds = 180,
    [string]$Registry = 'ccr.ccs.tencentyun.com',
    [string]$RegistryUser,
    [string]$RegistryPassword
)

$worker = Join-Path $PSScriptRoot 'windows-auto-update.ps1'
if (-not (Test-Path $worker)) { Write-Host "找不到 $worker" -ForegroundColor Red; exit 1 }

& $worker -RepoDir $RepoDir -WaitHealthySeconds $WaitHealthySeconds -Registry $Registry `
    -RegistryUser $RegistryUser -RegistryPassword $RegistryPassword -Open

exit $LASTEXITCODE
