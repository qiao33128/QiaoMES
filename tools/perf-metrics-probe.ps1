# 预聚合效果对比（阶段 4 · 4.6）
# 对比同一报表接口在「实时聚合明细」与「读预聚合汇总表」两种路径下的耗时。
# 用法：pwsh -File tools/perf-metrics-probe.ps1 [-SeedCount 1000000] [-Days 7] [-Iterations 20]

param(
    [int]$SeedCount = 1000000,
    [int]$Days = 7,
    [int]$Iterations = 20,
    [string]$ApiUrl = 'http://localhost:5199'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\QiaoMES.Api'
$logPath = Join-Path $PSScriptRoot 'perf-metrics.log'

function Wait-Api {
    param([string]$Url)
    for ($i = 0; $i -lt 90; $i++) {
        try {
            Invoke-WebRequest -Uri "$Url/api/auth/login" -Method Post -TimeoutSec 3 `
                -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck | Out-Null
            return $true
        }
        catch { Start-Sleep -Seconds 2 }
    }
    return $false
}

function Measure-Endpoint {
    param([string]$Url, [hashtable]$Headers, [int]$N)

    $samples = @()
    for ($i = 0; $i -lt $N; $i++) {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        Invoke-RestMethod -Uri $Url -Headers $Headers -TimeoutSec 180 | Out-Null
        $watch.Stop()
        $samples += $watch.ElapsedMilliseconds
    }

    $sorted = $samples | Sort-Object
    return [ordered]@{
        iterations = $N
        p50Ms      = $sorted[[math]::Min($N - 1, [math]::Floor($N * 0.5))]
        p95Ms      = $sorted[[math]::Min($N - 1, [math]::Ceiling($N * 0.95) - 1)]
        maxMs      = $sorted[-1]
        meanMs     = [math]::Round(($samples | Measure-Object -Average).Average, 1)
    }
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'

$process = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', $project, '--urls', $ApiUrl, '--no-launch-profile') `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

try {
    if (-not (Wait-Api -Url $ApiUrl)) { throw 'API 未就绪' }

    $login = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/auth/login" `
        -ContentType 'application/json' -Body (@{ username = 'admin'; password = 'Admin123!' } | ConvertTo-Json)
    $headers = @{ Authorization = "Bearer $($login.accessToken)" }

    if ($SeedCount -gt 0) {
        Write-Host "[0/4] 造种子数据 $SeedCount 行 SN（无检验单）..." -ForegroundColor Cyan
        Invoke-RestMethod -Method Post -Headers $headers -TimeoutSec 900 `
            -Uri "$ApiUrl/api/performance/seed?snCount=$SeedCount&inspectionCount=0" | Out-Null
    }

    $today = (Get-Date).ToString('yyyy-MM-dd')
    $from = (Get-Date).AddDays(-$Days).ToString('yyyy-MM-dd')
    $range = "from=$from&to=$today"

    Write-Host "[1/4] 明细实时聚合（汇总表未覆盖 $Days 天，自动回退实时）..." -ForegroundColor Cyan
    $realtime = Measure-Endpoint -Url "$ApiUrl/api/reports/shifts?$range" -Headers $headers -N $Iterations
    Write-Host ("  shifts 实时聚合：P50={0} ms  P95={1} ms  max={2} ms" -f $realtime.p50Ms, $realtime.p95Ms, $realtime.maxMs) -ForegroundColor Yellow

    Write-Host "[2/4] 重算预聚合..." -ForegroundColor Cyan
    $rebuild = Invoke-RestMethod -Method Post -Headers $headers -TimeoutSec 900 `
        -Uri "$ApiUrl/api/reports/rebuild-metrics?$range"
    Write-Host ("  已重算 {0} 个班次" -f $rebuild.shiftsRebuilt)

    Write-Host "[3/4] 读预聚合汇总表..." -ForegroundColor Cyan
    $precomputed = Measure-Endpoint -Url "$ApiUrl/api/reports/shifts?$range" -Headers $headers -N $Iterations
    Write-Host ("  shifts 读汇总：P50={0} ms  P95={1} ms  max={2} ms" -f $precomputed.p50Ms, $precomputed.p95Ms, $precomputed.maxMs) -ForegroundColor Green

    Write-Host "[4/4] OEE 接口（同为汇总读路径）..." -ForegroundColor Cyan
    $oee = Measure-Endpoint -Url "$ApiUrl/api/reports/oee?$range" -Headers $headers -N $Iterations
    Write-Host ("  oee    读汇总：P50={0} ms  P95={1} ms  max={2} ms" -f $oee.p50Ms, $oee.p95Ms, $oee.maxMs) -ForegroundColor Green

    $speedup = if ($precomputed.p95Ms -gt 0) { [math]::Round($realtime.p95Ms / $precomputed.p95Ms, 1) } else { 'n/a' }
    Write-Host ("\nP95 改善：{0} ms → {1} ms（约 {2}x）" -f $realtime.p95Ms, $precomputed.p95Ms, $speedup) -ForegroundColor Magenta

    $report = [ordered]@{
        generatedAt        = (Get-Date).ToString('O')
        range              = @{ from = $from; to = $today; days = $Days }
        seedCount          = $SeedCount
        realtimeAggregation = $realtime
        precomputedRead    = $precomputed
        oeePrecomputedRead = $oee
        p95Speedup         = $speedup
        rebuild            = $rebuild
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'perf-metrics-report.json') -Encoding UTF8
    Write-Host '报告：tools/perf-metrics-report.json' -ForegroundColor Green
}
finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
