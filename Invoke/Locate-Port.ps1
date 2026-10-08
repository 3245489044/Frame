<#
.SYNOPSIS
    【入口】诊断相位 · 零运行定位（端口键 → 声明 / 实现 / 消费用例 / 工程）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Get-PortPlan.ps1。
    诊断相位（P0）：只读 / 零裁决 / 零试跑；把「改一个端口键到底要碰哪些文件」一次拿全
    （G/E1 根治，与 Locate-Test.ps1 的「测试落点」、Locate-Build.ps1 的「改动→计划」对称）。
    索引源 = TestRunner\TestRunner.psm1 的 Get-PortDependencyIndex（零运行结构事实）。
    未命中判 unmapped（fail-loud，≠「不存在」）；诊断运行不产 pass/fail，不计入验证账。
.PARAMETER Key
    端口键（可多值；多值 = OR，如 'DecisionRecorder.AuditProbe'）。
.PARAMETER All
    输出全部已索引端口键（清点）。
.PARAMETER Json
    以 JSON 输出计划（供机器消费 / 冻结）。
.PARAMETER OutFile
    将 JSON 计划写入该路径（冻结清单）。
.PARAMETER ShowDetail
    文本模式下列出逐条消费用例。
.EXAMPLE
    .\Locate-Port.ps1 -Key DecisionRecorder.AuditProbe
.EXAMPLE
    .\Locate-Port.ps1 -All -Json
#>
param(
    [string[]]$Key = @(),

    [switch]$All,

    [switch]$Json,

    [string]$OutFile = '',

    [switch]$ShowDetail
)

$target = Join-Path $PSScriptRoot "..\..\Environment\CentralTest\Get-PortPlan.ps1"

$params = @{}
if ($PSBoundParameters.ContainsKey('Key')) { $params['Key'] = $Key }
if ($All)        { $params['All'] = $true }
if ($Json)       { $params['Json'] = $true }
if ($ShowDetail) { $params['ShowDetail'] = $true }
if ($PSBoundParameters.ContainsKey('OutFile') -and $OutFile) { $params['OutFile'] = $OutFile }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
