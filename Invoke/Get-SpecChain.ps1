<#
.SYNOPSIS
    【入口】链单元（chain / use）只读清单（施工卡 2-2 · 读侧 · D10=(a)）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Tools\Get-SpecChain.ps1。
    只读 / 零写盘 / 幂等：列清 D4′ 减体量的**分析前置**（已声明链 · 用例 use 引用 · 内联载荷去重候选）。
    诊断相位用途；不产出 pass/fail 结论，不作放行依据。
.PARAMETER CasesRoot
    用例根（缺省 = AgentTest；仅收路径含 \Cases\ 的 *.specs）。
.PARAMETER Out
    清单 JSON 出口（显式给定才写盘）。
.PARAMETER MinShare
    内联载荷去重候选的最小复用次数（缺省 2）。
.PARAMETER Explain
    逐条明细输出。
.PARAMETER Check
    自检模式（正/负对照，防解析器空转）；违规 exit 1。
.EXAMPLE
    .\Get-SpecChain.ps1 -Explain
.EXAMPLE
    .\Get-SpecChain.ps1 -Check
#>
param(
    [string]$CasesRoot = '',

    [string]$Out = '',

    [int]$MinShare = 2,

    [switch]$Explain,

    [switch]$Check
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\Tools\Get-SpecChain.ps1'

$params = @{}
if ($PSBoundParameters.ContainsKey('CasesRoot')) { $params['CasesRoot'] = $CasesRoot }
if ($PSBoundParameters.ContainsKey('Out')) { $params['Out'] = $Out }
if ($PSBoundParameters.ContainsKey('MinShare')) { $params['MinShare'] = $MinShare }
if ($PSBoundParameters.ContainsKey('Explain')) { $params['Explain'] = $true }
if ($PSBoundParameters.ContainsKey('Check')) { $params['Check'] = $true }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
