<#
.SYNOPSIS
    【入口】诊断相位 · 零运行定位（改动文件 → 构建 / 测试 / 门禁清单）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Get-VerifyPlan.ps1。
    诊断相位（P0）：只读 / 零裁决 / 零试跑；把「改动 → 该构建什么 / 该跑什么测试 / 该跑哪些门禁」
    一次拿全（G6，与 Locate-Test.ps1 的「测试落点」反查对称）。产出可冻结清单供 Invoke-Verify.ps1 消费。
    未命中判 Unmapped（fail-loud，≠「全部」；禁静默全量）。诊断运行不产出 pass/fail，不计入验证账。
.PARAMETER Changed
    改动文件（仓库相对路径或绝对路径；多值）。
.PARAMETER Configuration
    构建配置（默认 Debug；仅登记，不触发构建）。
.PARAMETER TestSet
    产品改动时须运行的测试矩阵（default/agent/infra；默认 default）。
.PARAMETER Json
    以 JSON 输出计划（供机器消费 / 冻结）。
.PARAMETER OutFile
    将 JSON 计划写入该路径（冻结清单）。
.EXAMPLE
    .\Locate-Build.ps1 -Changed 'Frame\CsharpPrj\Share\X.cs'
.EXAMPLE
    .\Locate-Build.ps1 -Changed 'Frame\PowerShellScript\Operation\DSL\Check-VerifySingleShot.ps1' -Json
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Changed,

    [string]$Configuration = 'Debug',

    [ValidateSet('default', 'agent', 'infra')]
    [string]$TestSet = 'default',

    [switch]$Json,

    [string]$OutFile = ''
)

$target = Join-Path $PSScriptRoot "..\..\Environment\CentralTest\Get-VerifyPlan.ps1"

$params = @{ Changed = $Changed }
if ($PSBoundParameters.ContainsKey('Configuration')) { $params['Configuration'] = $Configuration }
if ($PSBoundParameters.ContainsKey('TestSet'))       { $params['TestSet'] = $TestSet }
if ($Json) { $params['Json'] = $true }
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
