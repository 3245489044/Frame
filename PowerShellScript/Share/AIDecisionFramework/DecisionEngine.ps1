# DecisionEngine.ps1 - 核心决策引擎
# 实现《AI决策框架》第五章的工程决策矩阵
# 基于执行次数 N 和任务耗时 T 输出推荐模式

<#
.SYNOPSIS
    获取执行模式决策
.DESCRIPTION
    基于《AI决策框架》第五章的决策矩阵，根据任务参数推荐执行模式。
    决策树逻辑：
    - 逻辑不闭合 → 强制实时 ReAct
    - N=1, T<1s → 实时终端
    - N=1, T<10min → 实时 Agent（串行）
    - N=1, T>1h → 强制离线脚本（异步）
    - N≥5 → 离线脚本/编译
.PARAMETER TaskDesc
    任务描述
.PARAMETER N
    执行次数
.PARAMETER T
    单次任务耗时（分钟）
.PARAMETER LogicClosed
    逻辑是否闭合（可预生成脚本）
.PARAMETER Complexity
    任务复杂度
.EXAMPLE
    Get-ExecutionMode -TaskDesc "批量重命名文件" -N 1 -T 2 -LogicClosed $true
    Get-ExecutionMode -TaskDesc "每日日志分析" -N 10 -T 30 -LogicClosed $true
#>
function Get-ExecutionMode {
    param(
        [string]$TaskDesc = "未命名任务",

        [Parameter(Mandatory)]
        [int]$N,

        [double]$T = 5.0,

        [bool]$LogicClosed = $true,

        [ValidateSet("low", "medium", "high")]
        [string]$Complexity = "medium"
    )

    # 决策树逻辑
    $mode = ""
    $reason = ""
    $economicAdvice = ""

    # 获取动态经济数值用于决策理由
    $restartTax = Get-CognitiveRestartTax
    $realtimeTokenCost = Get-TokenCost -Mode "realtime" -Complexity $Complexity
    $scriptTokenCost = Get-TokenCost -Mode "script" -Complexity $Complexity
    $hourlyRate = Get-FrameworkConfig "HourlyRate"

    if (-not $LogicClosed) {
        # 逻辑不闭合，无法预生成脚本
        $mode = "强制实时 ReAct"
        $reason = "逻辑不闭合，脚本无法预生成，物理上不可行"
        $economicAdvice = "无选择，必须使用实时模式"
    }
    elseif ($N -eq 1 -and $T -le 1.0 / 60.0) {
        # 简单别名，≤1秒（T 单位为分钟，1/60 ≈ 0.0167）
        $mode = "实时终端"
        $reason = "写文件的 IO 耗时 > 直接敲命令，脚本收益为负"
        $economicAdvice = "直接敲命令，不要写脚本文件"
    }
    elseif ($N -eq 1 -and $T -le 10.0) {
        # 确定脚本，单次运行，≤10分钟
        $mode = "实时 Agent（串行）"
        $reason = "认知重启税（`$($restartTax.TaxUSD)）远大于节省的 Token（`$($realtimeTokenCost.CostUSD)），盯屏最划算"
        $economicAdvice = "使用实时 ReAct 模式串行执行，不要提交后台"
    }
    elseif ($N -eq 1 -and $T -gt 60.0) {
        # 确定脚本，单次但极耗时，>1小时
        $mode = "强制离线脚本（异步）"
        $reason = "人类阻塞成本（`$$hourlyRate/小时）远超 `$($restartTax.TaxUSD) 的重启税，必须释放人力"
        $economicAdvice = "编写脚本提交后台运行，人去做其他事"
    }
    elseif ($N -ge 5) {
        # 确定脚本，多次运行
        $mode = "离线脚本/编译"
        $reason = "高额沉没成本（含调试期望）被充分摊销"
        $economicAdvice = "编写可复用脚本，后续执行零 Token 消耗"
    }
    else {
        # 兜底：N=2~4 的中间地带
        $mode = "建议实时 Agent"
        $reason = "N=$N 未达到脚本化盈亏平衡点（N≥5），脚本收益为负"
        $economicAdvice = "先用实时模式执行，如果后续重复次数增加再考虑脚本化"
    }

    # 获取经济分析
    $amortAnalysis = Get-AmortizationAnalysis -N $N -Complexity $Complexity
    $totalCost = Get-TaskTotalCost -N $N -T $T -Complexity $Complexity

    return [PSCustomObject]@{
        TaskDesc = $TaskDesc
        Parameters = @{
            N = $N
            T = $T
            LogicClosed = $LogicClosed
            Complexity = $Complexity
        }
        Decision = @{
            Mode = $mode
            Reason = $reason
            EconomicAdvice = $economicAdvice
        }
        Economics = @{
            Amortization = $amortAnalysis
            TotalCost = $totalCost
        }
        Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    }
}

<#
.SYNOPSIS
    获取决策矩阵速查表
.DESCRIPTION
    返回《AI决策框架》第五章的完整决策矩阵
.EXAMPLE
    Get-DecisionMatrix
#>
function Get-DecisionMatrix {
    $matrix = @(
        [PSCustomObject]@{
            Scenario = "临时探索（查 API 字段）"
            N = "任意"
            T = "任意"
            LogicClosed = $false
            RecommendedMode = "强制实时 ReAct"
            CoreLogic = "逻辑不闭合，脚本无法预生成，物理上不可行"
        }
        [PSCustomObject]@{
            Scenario = "简单别名（如 ll）"
            N = "N=1"
            T = "< 1 秒"
            LogicClosed = $true
            RecommendedMode = "实时终端"
            CoreLogic = "写文件的 IO 耗时 > 直接敲命令，脚本收益为负"
        }
        [PSCustomObject]@{
            Scenario = "确定脚本，单次运行"
            N = "N=1"
            T = "< 10 分钟"
            LogicClosed = $true
            RecommendedMode = "实时 Agent（串行）"
            CoreLogic = "认知重启税（`$1.62）远大于节省的 Token（`$0.006），盯屏最划算"
        }
        [PSCustomObject]@{
            Scenario = "确定脚本，多次运行"
            N = "N ≥ 5"
            T = "任意"
            LogicClosed = $true
            RecommendedMode = "离线脚本/编译"
            CoreLogic = "高额沉没成本（含调试期望）被充分摊销"
        }
        [PSCustomObject]@{
            Scenario = "确定脚本，单次但极耗时"
            N = "N=1"
            T = "> 1 小时"
            LogicClosed = $true
            RecommendedMode = "强制离线脚本（异步）"
            CoreLogic = "人类阻塞成本（`$100/小时）远超 `$1.62 的重启税，必须释放人力"
        }
    )

    return $matrix
}

<#
.SYNOPSIS
    验证决策参数的有效性
.DESCRIPTION
    检查输入的参数是否在合理范围内
.PARAMETER N
    执行次数
.PARAMETER T
    任务耗时（分钟）
.EXAMPLE
    Test-DecisionParams -N 5 -T 30
#>
function Test-DecisionParams {
    param(
        [int]$N = 1,
        [double]$T = 5.0
    )

    $warnings = @()

    if ($N -lt 1) {
        $warnings += "执行次数 N 必须 ≥ 1"
    }
    if ($T -le 0) {
        $warnings += "任务耗时 T 必须 > 0"
    }
    if ($N -gt 10000) {
        $warnings += "执行次数 N 极大（$N），请确认是否合理"
    }
    if ($T -gt 1440) {
        $warnings += "单次耗时 T 超过 24 小时（$T 分钟），请确认是否合理"
    }

    return [PSCustomObject]@{
        IsValid = ($warnings.Count -eq 0)
        Warnings = $warnings
    }
}

# 注意：此模块通过 _init.ps1 的 dot-source 方式导入，函数直接可用
# 如果作为独立模块导入，请取消下面注释：
# Export-ModuleMember -Function Get-ExecutionMode, Get-DecisionMatrix, Test-DecisionParams
