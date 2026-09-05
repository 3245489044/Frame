# TokenAccounting.ps1 - Token 经济算账模块
# 实现《AI决策框架》文档中的成本计算公式
# 包含：含错期望成本、认知重启税、摊销分析

# 注意：Get-FrameworkConfig 已在 _init.ps1 中定义，此处不再重复

<#
.SYNOPSIS
    计算脚本模式的期望 Token 成本（含调试期望）
.DESCRIPTION
    基于文档 1.1 节的公式：
    E[Tokens] = 800 + 0.6 × (1500 + 600) + 0.2 × (1000 + 400)
.PARAMETER Complexity
    任务复杂度: "low" | "medium" | "high"
.EXAMPLE
    Get-TokenCost -Mode "script" -Complexity "medium"
#>
function Get-TokenCost {
    param(
        [Parameter(Mandatory)]
        [ValidateSet("script", "realtime")]
        [string]$Mode,

        [ValidateSet("low", "medium", "high")]
        [string]$Complexity = "medium"
    )

    $config = @{
        low = @{ initial = 400; err1_prob = 0.3; err1_in = 800; err1_out = 300; err2_prob = 0.1; err2_in = 500; err2_out = 200 }
        medium = @{ initial = 800; err1_prob = 0.6; err1_in = 1500; err1_out = 600; err2_prob = 0.2; err2_in = 1000; err2_out = 400 }
        high = @{ initial = 1500; err1_prob = 0.8; err1_in = 2500; err1_out = 1000; err2_prob = 0.4; err2_in = 2000; err2_out = 800 }
    }

    if ($Mode -eq "script") {
        $c = $config[$Complexity]
        $expected = $c.initial +
                    $c.err1_prob * ($c.err1_in + $c.err1_out) +
                    $c.err2_prob * ($c.err2_in + $c.err2_out)
        return [PSCustomObject]@{
            Mode = "Script"
            Complexity = $Complexity
            ExpectedTokens = [math]::Round($expected, 0)
            CostUSD = [math]::Round($expected * (Get-FrameworkConfig "TokenPrice"), 6)
            Breakdown = @{
                InitialGen = $c.initial
                FirstError = @{ Probability = $c.err1_prob; Tokens = $c.err1_in + $c.err1_out }
                SecondError = @{ Probability = $c.err2_prob; Tokens = $c.err2_in + $c.err2_out }
            }
        }
    }
    else {
        # 实时模式：生成动作 + 环境回显 + 历史累积摊销
        $c = $config[$Complexity]
        $genOut = $c.initial
        $echoIn = 2000
        $historyAmort = 500
        $total = $genOut + $echoIn + $historyAmort
        return [PSCustomObject]@{
            Mode = "Realtime"
            Complexity = $Complexity
            ExpectedTokens = $total
            CostUSD = [math]::Round($total * (Get-FrameworkConfig "TokenPrice"), 6)
            Breakdown = @{
                GenerateAction = $genOut
                EnvironmentEcho = $echoIn
                HistoryAmortization = $historyAmort
            }
        }
    }
}

<#
.SYNOPSIS
    计算认知重启税
.DESCRIPTION
    基于文档 2.1 节的公式：
    单次异步重启税 = 60 秒 × $0.027/秒 = $1.62
.PARAMETER RestartSeconds
    重启恢复时间（秒），默认 60
.EXAMPLE
    Get-CognitiveRestartTax
#>
function Get-CognitiveRestartTax {
    param(
        [double]$RestartSeconds = 60
    )

    $hourlyRate = Get-FrameworkConfig "HourlyRate"
    $ratePerSec = $hourlyRate / 3600.0
    $tax = $RestartSeconds * $ratePerSec

    return [PSCustomObject]@{
        RestartSeconds = $RestartSeconds
        HourlyRate = $hourlyRate
        TaxUSD = [math]::Round($tax, 4)
        Description = "认知重启税：${RestartSeconds}秒 × `$$([math]::Round($ratePerSec,4))/秒 = `$$([math]::Round($tax,4))"
    }
}

<#
.SYNOPSIS
    计算单次任务的真实总成本对比
.DESCRIPTION
    基于文档 2.2 节的对比表，返回实时模式 vs 脚本模式的综合成本
.PARAMETER N
    执行次数
.PARAMETER T
    单次任务耗时（分钟）
.PARAMETER Complexity
    任务复杂度
.EXAMPLE
    Get-TaskTotalCost -N 1 -T 5 -Complexity "medium"
#>
function Get-TaskTotalCost {
    param(
        [Parameter(Mandatory)]
        [int]$N,

        [double]$T = 5.0,

        [ValidateSet("low", "medium", "high")]
        [string]$Complexity = "medium"
    )

    $hourlyRate = Get-FrameworkConfig "HourlyRate"
    $ratePerSec = $hourlyRate / 3600.0
    $tokenPrice = Get-FrameworkConfig "TokenPrice"

    # Token 成本
    $scriptToken = Get-TokenCost -Mode "script" -Complexity $Complexity
    $realtimeToken = Get-TokenCost -Mode "realtime" -Complexity $Complexity

    # 人类阻塞等待（盯屏/提交）
    $scriptWaitSec = 4
    $realtimeWaitSec = 7

    # 认知重启税（仅脚本模式有）
    $restartTax = Get-CognitiveRestartTax

    # 实时模式总成本
    $realtimeTotal = ($realtimeToken.CostUSD * $N) + ($realtimeWaitSec * $ratePerSec * $N)

    # 脚本模式总成本（首次有重启税，后续无）
    $scriptTotal = ($scriptToken.CostUSD * $N) + ($scriptWaitSec * $ratePerSec * $N)
    if ($N -ge 1) {
        $scriptTotal += $restartTax.TaxUSD  # 首次提交有重启税
    }

    return [PSCustomObject]@{
        N = $N
        T = $T
        Complexity = $Complexity
        Realtime = [PSCustomObject]@{
            TokenCost = [math]::Round($realtimeToken.CostUSD * $N, 4)
            HumanWaitCost = [math]::Round($realtimeWaitSec * $ratePerSec * $N, 4)
            CognitiveTax = 0
            TotalUSD = [math]::Round($realtimeTotal, 4)
        }
        Script = [PSCustomObject]@{
            TokenCost = [math]::Round($scriptToken.CostUSD * $N, 4)
            HumanWaitCost = [math]::Round($scriptWaitSec * $ratePerSec * $N, 4)
            CognitiveTax = [math]::Round($restartTax.TaxUSD, 4)
            TotalUSD = [math]::Round($scriptTotal, 4)
        }
        Ratio = if ($scriptTotal -ne 0) { [math]::Round($realtimeTotal / $scriptTotal, 2) } else { "N/A" }
    }
}

<#
.SYNOPSIS
    计算脚本化收益的摊销分析
.DESCRIPTION
    基于文档 3.1 节的公式：
    脚本化净收益 = N × (R - S) - F
.PARAMETER N
    执行次数
.PARAMETER Complexity
    任务复杂度
.EXAMPLE
    Get-AmortizationAnalysis -N 5 -Complexity "medium"
#>
function Get-AmortizationAnalysis {
    param(
        [Parameter(Mandatory)]
        [int]$N,

        [ValidateSet("low", "medium", "high")]
        [string]$Complexity = "medium"
    )

    $hourlyRate = Get-FrameworkConfig "HourlyRate"
    $ratePerSec = $hourlyRate / 3600.0
    $tokenPrice = Get-FrameworkConfig "TokenPrice"

    # R = 确定性任务单次实时成本
    $realtimeToken = Get-TokenCost -Mode "realtime" -Complexity $Complexity
    $R = $realtimeToken.CostUSD + (7 * $ratePerSec)  # Token + 人力等待

    # S = 单次脚本边际执行成本（近似为 0）
    $S = 0.0

    # F = 脚本固定编写与调试沉没成本
    $scriptToken = Get-TokenCost -Mode "script" -Complexity $Complexity
    $restartTax = Get-CognitiveRestartTax
    $F = $scriptToken.CostUSD + (4 * $ratePerSec) + $restartTax.TaxUSD

    $netBenefit = $N * ($R - $S) - $F
    $breakEvenN = if (($R - $S) -gt 0) { [math]::Ceiling($F / ($R - $S)) } else { [int]::MaxValue }

    return [PSCustomObject]@{
        N = $N
        Complexity = $Complexity
        R_SingleRealtime = [math]::Round($R, 4)
        S_SingleScriptMarginal = [math]::Round($S, 4)
        F_FixedSunkCost = [math]::Round($F, 4)
        NetBenefit = [math]::Round($netBenefit, 4)
        BreakEvenN = $breakEvenN
        IsProfitable = $netBenefit -gt 0
        Recommendation = if ($netBenefit -gt 0) {
            "✅ 脚本化盈利：N=$N 已超过盈亏平衡点 N=$breakEvenN，建议使用脚本模式"
        } else {
            "❌ 脚本化亏损：N=$N 未达到盈亏平衡点 N=$breakEvenN，建议使用实时模式"
        }
    }
}

# 注意：此模块通过 _init.ps1 的 dot-source 方式导入，函数直接可用
# 如果作为独立模块导入，请取消下面注释：
# Export-ModuleMember -Function Get-TokenCost, Get-CognitiveRestartTax, Get-TaskTotalCost, Get-AmortizationAnalysis
