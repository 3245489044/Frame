#!/usr/bin/env pwsh
<#
.SYNOPSIS
    AI 决策框架主入口脚本
.DESCRIPTION
    基于《AI决策框架》文档，根据任务参数推荐执行模式。
    所有操作基于 PowerShell 实现，无需依赖 MCP 工具。
.PARAMETER TaskDesc
    任务描述
.PARAMETER N
    执行次数（默认 1）
.PARAMETER T
    单次任务耗时（分钟，默认 5）
.PARAMETER LogicClosed
    逻辑是否闭合（默认 true）
.PARAMETER Complexity
    任务复杂度: low | medium | high（默认 medium）
.PARAMETER ShowMatrix
    显示决策矩阵速查表
.PARAMETER ShowEconomics
    显示详细经济算账
.PARAMETER OutputJson
    以 JSON 格式输出结果
.PARAMETER OutputFile
    将结果输出到文件
.EXAMPLE
    # 基本用法：单次运行，耗时 2 分钟
    .\PowerShellScript\AIDecisionFramework\Invoke-Decision.ps1 -TaskDesc "批量重命名文件" -N 1 -T 2

    # 高频任务：每日运行，耗时 30 分钟
    .\PowerShellScript\AIDecisionFramework\Invoke-Decision.ps1 -TaskDesc "日志分析" -N 10 -T 30

    # 查看决策矩阵
    .\PowerShellScript\AIDecisionFramework\Invoke-Decision.ps1 -ShowMatrix

    # JSON 输出
    .\PowerShellScript\AIDecisionFramework\Invoke-Decision.ps1 -TaskDesc "测试" -N 5 -OutputJson
#>

[CmdletBinding()]
param(
    [string]$TaskDesc = "",

    [int]$N = 1,

    [double]$T = 5.0,

    [bool]$LogicClosed = $true,

    [ValidateSet("low", "medium", "high")]
    [string]$Complexity = "medium",

    [switch]$ShowMatrix,

    [switch]$ShowEconomics,

    [switch]$OutputJson,

    [string]$OutputFile = ""
)

# 初始化模块（_init.ps1 会自动计算 ScriptDir）
. "$PSScriptRoot\_init.ps1"

# 如果只查看决策矩阵
if ($ShowMatrix) {
    $matrix = Get-DecisionMatrix
    Write-Host "`n==========================================" -ForegroundColor Cyan
    Write-Host "  AI 决策框架 - 工程决策矩阵" -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host ""

    $matrix | ForEach-Object {
        Write-Host "场景: $($_.Scenario)" -ForegroundColor Yellow
        Write-Host "  N=$($_.N), T=$($_.T), 逻辑闭合=$($_.LogicClosed)"
        Write-Host "  推荐: $($_.RecommendedMode)" -ForegroundColor Green
        Write-Host "  逻辑: $($_.CoreLogic)"
        Write-Host ""
    }
    return
}

# 验证参数
$validation = Test-DecisionParams -N $N -T $T
if (-not $validation.IsValid) {
    Write-Host "❌ 参数验证失败:" -ForegroundColor Red
    $validation.Warnings | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

# 如果没有提供任务描述，使用默认
if ([string]::IsNullOrEmpty($TaskDesc)) {
    $TaskDesc = "未命名任务 (N=$N, T=${T}min)"
}

# 执行决策
Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host "  AI 决策框架 - 工程决策引擎" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "📋 任务: $TaskDesc" -ForegroundColor White
Write-Host "  参数: N=$N, T=${T}min, 逻辑闭合=$(if($LogicClosed){'是'}else{'否'}), 复杂度=$Complexity"
Write-Host ""

$decision = Get-ExecutionMode -TaskDesc $TaskDesc -N $N -T $T -LogicClosed $LogicClosed -Complexity $Complexity

Write-Host "🔍 决策结果:" -ForegroundColor Yellow
Write-Host "  推荐模式: $($decision.Decision.Mode)" -ForegroundColor Green
Write-Host "  原因: $($decision.Decision.Reason)"
Write-Host "  经济建议: $($decision.Decision.EconomicAdvice)"
Write-Host ""

if ($ShowEconomics) {
    Write-Host "💰 经济算账:" -ForegroundColor Yellow
    $eco = $decision.Economics

    Write-Host "  [摊销分析]"
    Write-Host "    单次实时成本 R = `$$($eco.Amortization.R_SingleRealtime)"
    Write-Host "    固定沉没成本 F = `$$($eco.Amortization.F_FixedSunkCost)"
    Write-Host "    净收益 = `$$($eco.Amortization.NetBenefit)"
    Write-Host "    盈亏平衡点 N = $($eco.Amortization.BreakEvenN)"
    Write-Host "    $($eco.Amortization.Recommendation)"
    Write-Host ""

    Write-Host "  [总成本对比]"
    Write-Host "    实时模式总成本 = `$$($eco.TotalCost.Realtime.TotalUSD)"
    Write-Host "      Token 成本: `$$($eco.TotalCost.Realtime.TokenCost)"
    Write-Host "      人力等待: `$$($eco.TotalCost.Realtime.HumanWaitCost)"
    Write-Host "    脚本模式总成本 = `$$($eco.TotalCost.Script.TotalUSD)"
    Write-Host "      Token 成本: `$$($eco.TotalCost.Script.TokenCost)"
    Write-Host "      人力等待: `$$($eco.TotalCost.Script.HumanWaitCost)"
    Write-Host "      认知重启税: `$$($eco.TotalCost.Script.CognitiveTax)"
    Write-Host "    成本比 (实时/脚本) = $($eco.TotalCost.Ratio)"
    Write-Host ""
}

# 输出到文件（统一处理）
if ($OutputFile) {
    if ($OutputJson) {
        $decision | ConvertTo-Json -Depth 5 | Out-File -FilePath $OutputFile -Encoding UTF8
    } else {
        $decision | Export-Clixml -Path $OutputFile
    }
    Write-Host "  已保存到: $OutputFile" -ForegroundColor Green
}

# JSON 控制台输出
if ($OutputJson) {
    Write-Host "📄 JSON 输出:" -ForegroundColor Yellow
    $decision | ConvertTo-Json -Depth 5 | Write-Host
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""
