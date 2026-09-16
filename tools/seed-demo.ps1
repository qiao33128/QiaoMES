<#
.SYNOPSIS
    一键生成 QiaoMES 演示数据（登录 → 调 /api/dev/demo-data）。

.DESCRIPTION
    往库里灌入一套可复现的 SMT 业务数据：3 条产线、3 个产品（含 BOM 与工艺路线）、
    8 张工单（草稿 → 已完工）、每单若干 SN 与完整过站轨迹、来料批次与 IQC、IPQC/FQC 检验单、
    不合格处置、设备台账与停机历史、Andon 呼叫，并按最近 N 天铺开时间线、重算预聚合指标。

    幂等：每次调用都会先清理旧的 DEMO- 数据再重建，可反复执行。
    安全：只删除业务编码以 DEMO- 开头的数据，手工录入的数据不受影响。

.PARAMETER BaseUrl
    服务地址。本地 docker compose 默认 http://localhost:8080，服务器按实际域名/端口填。

.PARAMETER Days
    数据铺开的天数（1~180），默认 30。

.PARAMETER WorkOrders
    工单数量（4~60），默认 8。

.PARAMETER SnPerOrder
    每张已下达工单生成的 SN 数量（10~500），默认 50。

.PARAMETER Cleanup
    只清理演示数据，不重新生成。

.EXAMPLE
    pwsh tools/seed-demo.ps1
    pwsh tools/seed-demo.ps1 -BaseUrl https://mes.qiaoqiaoqiao.me -Days 45 -SnPerOrder 80
    pwsh tools/seed-demo.ps1 -Cleanup
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:8080',
    [string]$Username = 'admin',
    [string]$Password = 'Admin123!',
    [int]$Days = 30,
    [int]$WorkOrders = 8,
    [int]$SnPerOrder = 50,
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'
$base = $BaseUrl.TrimEnd('/')

function Write-Step([string]$message) {
    Write-Host "==> $message" -ForegroundColor Cyan
}

# ---------- 1. 登录拿 JWT ----------
Write-Step "登录 $base（$Username）"
$loginBody = @{ username = $Username; password = $Password } | ConvertTo-Json -Compress
try {
    $login = Invoke-RestMethod -Method Post -Uri "$base/api/auth/login" -ContentType 'application/json' -Body $loginBody
}
catch {
    Write-Host "登录失败：$($_.Exception.Message)" -ForegroundColor Red
    Write-Host '如果是非开发环境，请确认 DemoData:Enabled = true 已设置。' -ForegroundColor Yellow
    exit 1
}

$token = $login.accessToken
if (-not $token) { $token = $login.token }
if (-not $token) {
    Write-Host '登录响应里没有找到 accessToken，请检查接口返回结构。' -ForegroundColor Red
    $login | ConvertTo-Json -Depth 4 | Write-Host
    exit 1
}

$headers = @{ Authorization = "Bearer $token" }

# ---------- 2. 当前演示数据规模 ----------
Write-Step '查询当前演示数据条数'
$before = Invoke-RestMethod -Method Get -Uri "$base/api/dev/demo-data" -Headers $headers
$before.counts.PSObject.Properties | ForEach-Object { "    {0,-18} {1}" -f $_.Name, $_.Value } | Write-Host

if ($Cleanup) {
    Write-Step '清理演示数据'
    $cleaned = Invoke-RestMethod -Method Delete -Uri "$base/api/dev/demo-data?days=$Days" -Headers $headers
    Write-Host "    已删除 $($cleaned.removedRows) 行" -ForegroundColor Green
    exit 0
}

# ---------- 3. 生成（先清理再重建，耗时较长） ----------
Write-Step "生成演示数据（days=$Days workOrders=$WorkOrders snPerOrder=$SnPerOrder，请稍候…）"
$watch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $summary = Invoke-RestMethod -Method Post -Headers $headers -TimeoutSec 900 `
        -Uri "$base/api/dev/demo-data?days=$Days&workOrders=$WorkOrders&snPerOrder=$SnPerOrder"
}
catch {
    Write-Host "生成失败：$($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) { Write-Host $_.ErrorDetails.Message -ForegroundColor DarkGray }
    exit 1
}
$watch.Stop()

# ---------- 4. 结果 ----------
Write-Host ''
Write-Host "完成，用时 $([math]::Round($watch.Elapsed.TotalSeconds, 1)) 秒：" -ForegroundColor Green
$summary.PSObject.Properties |
    Where-Object { $_.Name -ne 'note' } |
    ForEach-Object { "    {0,-22} {1}" -f $_.Name, $_.Value } |
    Write-Host

if ($summary.note) {
    Write-Host ''
    Write-Host $summary.note -ForegroundColor Gray
}
