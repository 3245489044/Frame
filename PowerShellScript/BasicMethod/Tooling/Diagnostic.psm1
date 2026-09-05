<#
.SYNOPSIS
    诊断框架函数模块 — 提供通用的诊断/验证结果收集、统计和报告功能

.DESCRIPTION
    从 DockerCommon.psm1 中提取的通用诊断框架，去除了 Docker 特定引用。
    适用于任何需要"收集检查结果 → 统计 → 健康度评分 → 问题诊断"的场景。

    包含函数:
        New-DiagnosticContext     — 初始化诊断上下文
        Write-Diagnostic          — 记录诊断结果
        Show-DiagnosticSummary    — 显示诊断总结（含健康度评分和问题诊断）
        Save-DiagnosticReport     — 保存诊断报告到文件

    用法:
        Import-Module .\BasicMethod-Diagnostic.psm1
        New-DiagnosticContext
        Write-Diagnostic "类别" "项目" "PASS" "描述"
        Show-DiagnosticSummary -Title "验证总结" -ExtraChecks @(
            @{ Pattern = "错误.*FAIL"; Message = "关键问题"; Level = "RED"; Suggestion = "建议" }
        )
        Save-DiagnosticReport -Path "report.txt"
#>

# ============================================
# 诊断框架函数
# ============================================

function New-DiagnosticContext {
    <#
    .SYNOPSIS
        初始化诊断上下文（重置统计计数器）
    .DESCRIPTION
        创建新的诊断上下文，重置 Results/PassCount/FailCount/WarnCount。
        所有诊断/验证脚本应在开始时调用此函数。
    .EXAMPLE
        New-DiagnosticContext
    #>
    $Global:DiagnosticResults = @()
    $Global:DiagnosticPassCount = 0
    $Global:DiagnosticFailCount = 0
    $Global:DiagnosticWarnCount = 0
}

function Write-Diagnostic {
    <#
    .SYNOPSIS
        记录一条诊断结果
    .DESCRIPTION
        记录诊断结果到全局变量，同时输出到控制台。
        支持 PASS / FAIL / WARN / INFO 四种状态。
    .PARAMETER Category
        诊断类别（如 "环境检查"、"服务测试"）
    .PARAMETER Item
        诊断项目名称
    .PARAMETER Status
        状态: PASS / FAIL / WARN / INFO
    .PARAMETER Message
        描述消息
    .EXAMPLE
        Write-Diagnostic "环境检查" "Docker 引擎" "PASS" "版本 24.0.6"
        Write-Diagnostic "服务测试" "端口 3100" "FAIL" "连接超时"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Category,

        [Parameter(Mandatory = $true)]
        [string]$Item,

        [Parameter(Mandatory = $true)]
        [ValidateSet("PASS", "FAIL", "WARN", "INFO")]
        [string]$Status,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $icon = switch ($Status) {
        "PASS" { "✅" }
        "FAIL" { "❌" }
        "WARN" { "⚠️ " }
        "INFO" { "ℹ️ " }
    }
    $line = "$icon [$Status] $Category / $Item`: $Message"
    $Global:DiagnosticResults += $line

    switch ($Status) {
        "PASS" { Write-Host $line -ForegroundColor Green; $Global:DiagnosticPassCount++ }
        "FAIL" { Write-Host $line -ForegroundColor Red;   $Global:DiagnosticFailCount++ }
        "WARN" { Write-Host $line -ForegroundColor Yellow; $Global:DiagnosticWarnCount++ }
        "INFO" { Write-Host $line -ForegroundColor Gray }
    }
}

function Show-DiagnosticSummary {
    <#
    .SYNOPSIS
        显示诊断总结
    .DESCRIPTION
        显示诊断/验证的统计汇总、健康度评分和问题诊断。
        应在所有检查完成后调用。
    .PARAMETER Title
        总结标题（默认 "综合诊断结论"）
    .PARAMETER ExtraChecks
        额外的检查项哈希表数组，用于自定义问题诊断。
        每个检查项格式: @{ Pattern = "正则"; Message = "问题描述"; Level = "RED"/"YELLOW"; Suggestion = "建议" }
    .EXAMPLE
        Show-DiagnosticSummary
    .EXAMPLE
        Show-DiagnosticSummary -Title "验证总结" -ExtraChecks @(
            @{ Pattern = "引擎.*FAIL"; Message = "引擎不可用"; Level = "RED"; Suggestion = "请先修复引擎" }
        )
    #>
    param(
        [string]$Title = "综合诊断结论",
        [array]$ExtraChecks = @()
    )

    Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

    $total = $Global:DiagnosticPassCount + $Global:DiagnosticFailCount + $Global:DiagnosticWarnCount
    Write-Host ""
    Write-Host "  检查项总计: $total" -ForegroundColor White
    Write-Host "  ✅ 通过: $($Global:DiagnosticPassCount)" -ForegroundColor Green
    Write-Host "  ❌ 失败: $($Global:DiagnosticFailCount)" -ForegroundColor Red
    Write-Host "  ⚠️  警告: $($Global:DiagnosticWarnCount)" -ForegroundColor Yellow
    Write-Host ""

    # 计算健康度
    $healthScore = if ($total -gt 0) { [math]::Round(($Global:DiagnosticPassCount / $total) * 100, 0) } else { 0 }
    $healthBar = "█" * [math]::Floor($healthScore / 10) + "░" * (10 - [math]::Floor($healthScore / 10))
    $healthColor = if ($healthScore -ge 80) { "Green" } elseif ($healthScore -ge 50) { "Yellow" } else { "Red" }
    Write-Host "  健康度: $healthBar $healthScore%" -ForegroundColor $healthColor
    Write-Host ""

    # 诊断结论
    if ($Global:DiagnosticFailCount -eq 0 -and $Global:DiagnosticWarnCount -eq 0) {
        Write-Host "  🎉 所有检查通过！" -ForegroundColor Green
        return
    }

    Write-Host "  📋 问题诊断:" -ForegroundColor Cyan

    $allResults = $Global:DiagnosticResults -join "`n"

    foreach ($check in $ExtraChecks) {
        if ($allResults -match $check.Pattern) {
            $icon = if ($check.Level -eq "RED") { "🔴" } else { "🟡" }
            $color = if ($check.Level -eq "RED") { "Red" } else { "Yellow" }
            Write-Host "  $icon 关键问题: $($check.Message)" -ForegroundColor $color
            if ($check.Suggestion) {
                Write-Host "     建议: $($check.Suggestion)" -ForegroundColor Yellow
            }
        }
    }

    Write-Host ""
    Write-Host "  💡 后续步骤:" -ForegroundColor Cyan
    Write-Host "  1. 根据上述问题诊断逐一修复" -ForegroundColor Gray
    Write-Host "  2. 修复后重新运行诊断确认" -ForegroundColor Gray
}

function Save-DiagnosticReport {
    <#
    .SYNOPSIS
        保存诊断报告到文件
    .PARAMETER Path
        输出文件路径
    .EXAMPLE
        Save-DiagnosticReport -Path "C:\temp\diagnostic.txt"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $Global:DiagnosticResults | Out-File -FilePath $Path -Encoding UTF8
    Write-Host "`n  诊断报告已保存: $Path" -ForegroundColor Green
}

# ============================================================
# 连续监控
# ============================================================

function Start-ContinuousMonitor {
    <#
    .SYNOPSIS
        连续监控指定端口的服务状态
    .DESCRIPTION
        每隔指定秒数检测一次端口状态，持续监控直到手动停止（Ctrl+C）。
        支持 TCP 端口检测和 HTTP 端点检测。
    .PARAMETER Port
        要监控的端口号
    .PARAMETER Name
        服务名称（用于显示）
    .PARAMETER Interval
        检测间隔秒数（默认 5）
    .PARAMETER MaxCount
        最大检测次数（默认 0 = 无限）
    .PARAMETER HttpPath
        HTTP 检测路径（可选，提供后同时检测 HTTP 端点）
    .PARAMETER HostName
        主机地址（默认 localhost）
    .EXAMPLE
        Start-ContinuousMonitor -Port 3100 -Name "MCP 服务"
        Start-ContinuousMonitor -Port 3100 -Name "MCP 服务" -Interval 2 -MaxCount 10
    #>
    param(
        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [int]$Interval = 5,

        [int]$MaxCount = 0,

        [string]$HttpPath = "",

        [string]$HostName = "localhost"
    )

    $count = 0
    Write-Host "  📊 开始监控 [$Name] (端口 $Port)..." -ForegroundColor Cyan
    Write-Host "  按 Ctrl+C 停止监控" -ForegroundColor Gray
    Write-Host ""

    while ($MaxCount -eq 0 -or $count -lt $MaxCount) {
        $count++
        $timestamp = Get-Date -Format "HH:mm:ss"

        # TCP 检测
        $tcpOk = $false
        try {
            $tcpClient = New-Object System.Net.Sockets.TcpClient
            $asyncResult = $tcpClient.BeginConnect($HostName, $Port, $null, $null)
            $waitResult = $asyncResult.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds(3), $false)
            if ($waitResult -and $tcpClient.Connected) {
                $tcpClient.EndConnect($asyncResult)
                $tcpOk = $true
            }
            $tcpClient.Close()
        } catch { }

        # HTTP 检测（可选）
        $httpOk = $false
        if ($HttpPath -and $tcpOk) {
            try {
                $url = "http://${HostName}:${Port}${HttpPath}"
                $webClient = New-Object System.Net.WebClient
                $null = $webClient.DownloadString($url)
                $webClient.Dispose()
                $httpOk = $true
            } catch { }
        }

        # 输出状态行
        $statusIcon = if ($tcpOk) { "🟢" } else { "🔴" }
        $statusText = if ($tcpOk) { "运行中" } else { "离线" }
        if ($HttpPath) {
            $httpIcon = if ($httpOk) { "✅" } else { "❌" }
            Write-Host "  [$timestamp] $statusIcon $Name (端口 $Port) $statusText | HTTP: $httpIcon" -ForegroundColor (if ($tcpOk) { "Green" } else { "Red" })
        } else {
            Write-Host "  [$timestamp] $statusIcon $Name (端口 $Port) $statusText" -ForegroundColor (if ($tcpOk) { "Green" } else { "Red" })
        }

        if ($MaxCount -gt 0 -and $count -ge $MaxCount) { break }
        Start-Sleep -Seconds $Interval
    }
}

# ============================================================
# JSON 诊断报告导出
# ============================================================

function Export-DiagnosticJson {
    <#
    .SYNOPSIS
        将诊断结果导出为 JSON 格式
    .DESCRIPTION
        将当前诊断上下文中的结果导出为结构化的 JSON 文件，
        包含统计信息和时间戳，便于程序化处理。
    .PARAMETER Path
        输出 JSON 文件路径
    .PARAMETER IncludeRaw
        是否包含原始结果行（默认 $true）
    .EXAMPLE
        Export-DiagnosticJson -Path "diagnostic-report.json"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [switch]$IncludeRaw = $true
    )

    $total = $Global:DiagnosticPassCount + $Global:DiagnosticFailCount + $Global:DiagnosticWarnCount

    $report = @{
        Timestamp   = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        Summary     = @{
            Total = $total
            Pass  = $Global:DiagnosticPassCount
            Fail  = $Global:DiagnosticFailCount
            Warn  = $Global:DiagnosticWarnCount
            HealthScore = if ($total -gt 0) { [math]::Round(($Global:DiagnosticPassCount / $total) * 100, 0) } else { 0 }
        }
    }

    if ($IncludeRaw -and $Global:DiagnosticResults) {
        $report.Results = $Global:DiagnosticResults
    }

    $json = $report | ConvertTo-Json -Depth 3
    $json | Out-File -FilePath $Path -Encoding UTF8
    Write-Host "`n  JSON 诊断报告已保存: $Path" -ForegroundColor Green
    return $report
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'New-DiagnosticContext',
    'Write-Diagnostic',
    'Show-DiagnosticSummary',
    'Save-DiagnosticReport',
    'Start-ContinuousMonitor',
    'Export-DiagnosticJson'
)
