<#
.SYNOPSIS
    诊断输出模块 — 提供统一的诊断结果输出函数（带 PASS/FAIL/WARN/INFO 状态）
.DESCRIPTION
    用于 Test-DockerDiagnostic.ps1、Test-McpServices.ps1 等诊断脚本。
    提供统一的诊断结果收集、计数和输出功能。

    用法:
        Import-Module .\Write-Diagnostic.psm1 -Force
        Initialize-Diagnostic
        Write-Diagnostic "分类" "项目" "PASS" "消息"
        Write-Diagnostic "分类" "项目" "FAIL" "消息"
        Show-DiagnosticSummary
#>

# 全局诊断结果存储
$script:DiagnosticResults = @()
$script:DiagnosticPassCount = 0
$script:DiagnosticFailCount = 0
$script:DiagnosticWarnCount = 0

function Initialize-Diagnostic {
    <#
    .SYNOPSIS
        初始化诊断计数器
    #>
    $script:DiagnosticResults = @()
    $script:DiagnosticPassCount = 0
    $script:DiagnosticFailCount = 0
    $script:DiagnosticWarnCount = 0
}

function Write-Diagnostic {
    <#
    .SYNOPSIS
        输出一条诊断结果
    .PARAMETER Category
        分类名称
    .PARAMETER Item
        检查项目名称
    .PARAMETER Status
        状态：PASS / FAIL / WARN / INFO
    .PARAMETER Message
        消息内容
    #>
    param(
        [string]$Category,
        [string]$Item,
        [string]$Status,
        [string]$Message
    )

    $icon = switch ($Status) {
        "PASS"  { "✅" }
        "FAIL"  { "❌" }
        "WARN"  { "⚠️ " }
        "INFO"  { "ℹ️ " }
        default { "  " }
    }
    $line = "$icon [$Status] $Category / $Item`: $Message"
    $script:DiagnosticResults += $line

    switch ($Status) {
        "PASS"  { Write-Host $line -ForegroundColor Green; $script:DiagnosticPassCount++ }
        "FAIL"  { Write-Host $line -ForegroundColor Red;   $script:DiagnosticFailCount++ }
        "WARN"  { Write-Host $line -ForegroundColor Yellow; $script:DiagnosticWarnCount++ }
        "INFO"  { Write-Host $line -ForegroundColor Gray }
        default { Write-Host $line }
    }
}

function Show-DiagnosticSummary {
    <#
    .SYNOPSIS
        显示诊断汇总信息
    .PARAMETER Title
        汇总标题（可选）
    #>
    param([string]$Title = "综合诊断结论")

    Write-Section $Title

    $total = $script:DiagnosticPassCount + $script:DiagnosticFailCount + $script:DiagnosticWarnCount
    Write-Host ""
    Write-Host "  检查项总计: $total" -ForegroundColor White
    Write-Host "  ✅ 通过: $($script:DiagnosticPassCount)" -ForegroundColor Green
    Write-Host "  ❌ 失败: $($script:DiagnosticFailCount)" -ForegroundColor Red
    Write-Host "  ⚠️  警告: $($script:DiagnosticWarnCount)" -ForegroundColor Yellow
    Write-Host ""

    # 计算健康度
    if ($total -gt 0) {
        $healthScore = [math]::Round(($script:DiagnosticPassCount / $total) * 100, 0)
        $healthBar = "█" * [math]::Floor($healthScore / 10) + "░" * (10 - [math]::Floor($healthScore / 10))
        $healthColor = if ($healthScore -ge 80) { "Green" } elseif ($healthScore -ge 50) { "Yellow" } else { "Red" }
        Write-Host "  健康度: $healthBar $healthScore%" -ForegroundColor $healthColor
        Write-Host ""
    }

    if ($script:DiagnosticFailCount -eq 0 -and $script:DiagnosticWarnCount -eq 0) {
        Write-Host "  🎉 所有检查通过！" -ForegroundColor Green
    }
}

function Save-DiagnosticReport {
    <#
    .SYNOPSIS
        保存诊断报告到文件
    .PARAMETER OutputPath
        输出文件路径
    #>
    param([string]$OutputPath)

    if ($OutputPath) {
        $script:DiagnosticResults | Out-File -FilePath $OutputPath -Encoding UTF8
        Write-Host "`n  诊断报告已保存: $OutputPath" -ForegroundColor Green
    }
}

function Get-DiagnosticResultsText {
    <#
    .SYNOPSIS
        获取所有诊断结果的文本（用于分析）
    #>
    return $script:DiagnosticResults -join "`n"
}

# 导出所有函数
Export-ModuleMember -Function Initialize-Diagnostic, Write-Diagnostic, Show-DiagnosticSummary,
    Save-DiagnosticReport, Get-DiagnosticResultsText
