# QiaoMES 性能体检脚本（阶段 4 · 4.6）
# 用法：pwsh -File tools/perf-probe.ps1 [-SnCount 1000000] [-InspectionCount 200000] [-SkipSeed] [-Cleanup]
#
# 动作：启动本地 API → 登录 → 造种子数据 → 索引体检 → 关键查询执行计划 → 并发基准测试 → 输出报告
# 说明：种子数据带 SN-PERF- / PERF-IQC- 前缀，可用 -Cleanup 一键清除，不影响业务数据。

param(
    [int]$SnCount = 1000000,
    [int]$InspectionCount = 200000,
    [int]$Iterations = 200,
    [int]$Concurrency = 8,
    [string]$ApiUrl = 'http://localhost:5199',
    [switch]$SkipSeed,
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\QiaoMES.Api'
$reportPath = Join-Path $PSScriptRoot 'perf-report.json'

function Wait-Api {
    param([string]$Url)
    for ($i = 0; $i -lt 90; $i++) {
        try {
            # 401/400 也算就绪：能返回 HTTP 响应说明进程已监听
            Invoke-WebRequest -Uri "$Url/api/auth/login" -Method Post -TimeoutSec 3 `
                -ContentType 'application/json' -Body '{}' -SkipHttpErrorCheck | Out-Null
            return $true
        }
        catch { Start-Sleep -Seconds 2 }
    }
    return $false
}

Write-Host "[1/6] 启动 API（$ApiUrl）..." -ForegroundColor Cyan
$logPath = Join-Path $PSScriptRoot 'perf-api.log'

# 必须显式 Development：否则会读取 appsettings.Production.json（连 docker 服务名 qiaomes-postgres）
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'

$process = Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', $project, '--urls', $ApiUrl, '--no-launch-profile') `
    -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $logPath -RedirectStandardError "$logPath.err"

try {
    if (-not (Wait-Api -Url $ApiUrl)) {
        Write-Host "--- API 标准输出尾部 ---" -ForegroundColor Red
        if (Test-Path $logPath) { Get-Content $logPath -Tail 40 | Write-Host }
        if (Test-Path "$logPath.err") { Get-Content "$logPath.err" -Tail 40 | Write-Host }
        throw "API 未能在 180 秒内就绪，请检查上面日志与数据库连接"
    }

    Write-Host "[2/6] 登录..." -ForegroundColor Cyan
    $login = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/auth/login" `
        -ContentType 'application/json' `
        -Body (@{ username = 'admin'; password = 'Admin123!' } | ConvertTo-Json)
    $headers = @{ Authorization = "Bearer $($login.accessToken)" }

    if ($Cleanup) {
        Write-Host "[3/6] 清理历史种子数据..." -ForegroundColor Yellow
        Invoke-RestMethod -Method Delete -Uri "$ApiUrl/api/performance/seed" -Headers $headers | ConvertTo-Json -Depth 5
        Write-Host "已清理，脚本结束。" -ForegroundColor Yellow
        return
    }

    if (-not $SkipSeed) {
        Write-Host "[3/6] 造种子数据（SN $SnCount 行 / 检验单 $InspectionCount 行，请耐心等待）..." -ForegroundColor Cyan
        $seed = Invoke-RestMethod -Method Post -Headers $headers `
            -Uri "$ApiUrl/api/performance/seed?snCount=$SnCount&inspectionCount=$InspectionCount" `
            -TimeoutSec 900
        $seed | ConvertTo-Json -Depth 5 | Write-Host
    }
    else {
        Write-Host "[3/6] 跳过造数据（-SkipSeed）" -ForegroundColor DarkGray
    }

    Write-Host "[4/6] 索引体检..." -ForegroundColor Cyan
    $indexHealth = Invoke-RestMethod -Uri "$ApiUrl/api/performance/index-health" -Headers $headers -TimeoutSec 120

    Write-Host "  · 顺序扫描最多的表（缺索引嫌疑）：" -ForegroundColor Gray
    $indexHealth.tables | Select-Object -First 10 |
        Format-Table schema, table, seq_scan, idx_scan, live_rows, total_size -AutoSize | Out-String | Write-Host

    Write-Host "  · 从未命中的索引：" -ForegroundColor Gray
    $indexHealth.unusedIndexes | Select-Object -First 10 |
        Format-Table schema, table, index, idx_scan, index_size -AutoSize | Out-String | Write-Host

    Write-Host "[5/6] 关键查询执行计划（EXPLAIN ANALYZE）..." -ForegroundColor Cyan
    $scenarios = @('sn-lookup', 'inspection-by-sn', 'sn-status-stats', 'inspection-daily', 'outbox-pending', 'andon-open')
    $plans = @{}
    foreach ($scenario in $scenarios) {
        $plan = Invoke-RestMethod -Uri "$ApiUrl/api/performance/explain?scenario=$scenario" -Headers $headers -TimeoutSec 300
        $plans[$scenario] = $plan

        # EXPLAIN 的 QUERY PLAN 列是 JSON 文本，需要二次解析
        $node = $null
        try {
            $rawText = $plan.plan[0].'QUERY PLAN'
            $parsed = $rawText | ConvertFrom-Json
            $top = if ($parsed -is [array]) { $parsed[0] } else { $parsed }
            $node = $top.Plan
        }
        catch { }

        if ($node) {
            $scanType = $node.'Node Type'
            $execMs = [math]::Round($node.'Actual Total Time', 3)
            Write-Host ("  · {0,-22} {1,-20} total={2,8} ms  rows={3}" -f `
                    $scenario, $scanType, $execMs, $node.'Actual Rows')
        }
        else {
            Write-Host ("  · {0,-22} （计划解析失败，详见报告 JSON）" -f $scenario) -ForegroundColor Yellow
        }
    }

    Write-Host "[6/6] 并发基准测试（iterations=$Iterations, concurrency=$Concurrency）..." -ForegroundColor Cyan
    $benchmarks = @()
    foreach ($scenario in $scenarios) {
        $result = Invoke-RestMethod -Headers $headers -TimeoutSec 900 `
            -Uri "$ApiUrl/api/performance/benchmark?scenario=$scenario&iterations=$Iterations&concurrency=$Concurrency"
        $benchmarks += $result
        Write-Host ("  · {0,-22} P50={1,6} ms  P95={2,6} ms  P99={3,6} ms  max={4,6} ms  {5} req/s" -f `
            $result.scenario, $result.p50Ms, $result.p95Ms, $result.p99Ms, $result.maxMs, $result.throughputPerSecond)
    }

    $report = [ordered]@{
        generatedAt = (Get-Date).ToString('O')
        seed = @{ snCount = $SnCount; inspectionCount = $InspectionCount }
        indexHealth = $indexHealth
        plans = $plans
        benchmarks = $benchmarks
    }
    $report | ConvertTo-Json -Depth 12 | Set-Content -Path $reportPath -Encoding UTF8
    Write-Host "`n报告已保存：$reportPath" -ForegroundColor Green
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Write-Host "API 已停止。" -ForegroundColor DarkGray
    }
}
