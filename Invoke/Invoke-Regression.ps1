<#
.SYNOPSIS
    【入口】回归测试一键运行
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Invoke-AgentRegression.ps1
    把逐工程 dotnet test 压成一条命令，接通已知红裁决。
.PARAMETER Matrix
    测试矩阵：default / agent / infra，默认 default。
.PARAMETER Test
    显式指定测试目标（别名表见 CentralTest\TestRunner\TestTargets.psd1）。
.PARAMETER Case
    定向声明式用例 caseId（R32 机制）。
.PARAMETER Parallel
    并发上限，默认 4。
.PARAMETER NoKnownRedOk
    关闭已知红裁决（默认开启）。
.PARAMETER WithGates
    回归通过后串接门禁检查。
.EXAMPLE
    .\Invoke-Regression.ps1 -Matrix default -Parallel 4
.EXAMPLE
    .\Invoke-Regression.ps1 -Case core.json-rule-engine.unknown-op.callback-logs
#>
param(
    [ValidateSet('default', 'agent', 'infra')]
    [string]$Matrix = 'default',

    [string[]]$Test,

    [string[]]$Case,

    [int]$Parallel = 4,

    [switch]$NoKnownRedOk,

    [switch]$WithGates
)

$target = Join-Path $PSScriptRoot "..\..\Environment\CentralTest\Invoke-AgentRegression.ps1"

$params = @{ Matrix = $Matrix }
if ($PSBoundParameters.ContainsKey('Test') -and $Test.Count -gt 0) { $params['Test'] = $Test }
if ($PSBoundParameters.ContainsKey('Case') -and $Case.Count -gt 0) { $params['Case'] = $Case }
if ($PSBoundParameters.ContainsKey('Parallel'))  { $params['Parallel'] = $Parallel }
if ($NoKnownRedOk) { $params['NoKnownRedOk'] = $true }
if ($WithGates)    { $params['WithGates'] = $true }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
