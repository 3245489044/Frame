<#
.SYNOPSIS
    【入口】把匹配用例的内联 invocation 切到既有链（施工卡 2-2 · 写侧）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Tools\Chain\Use-SpecChain.ps1。
    缺省 DryRun（零写盘）；-Write 落盘。三类结构自检（未改写 / 悬空 / 空产物）。
#>
param(
    [Parameter(Mandatory = $true)][string]$Suite,

    [Parameter(Mandatory = $true)][string]$Chain,

    [string]$Case = '',

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\Tools\Chain\Use-SpecChain.ps1'

$params = @{ Suite = $Suite; Chain = $Chain }
if ($PSBoundParameters.ContainsKey('Case')) { $params['Case'] = $Case }
if ($PSBoundParameters.ContainsKey('Write')) { $params['Write'] = $true }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
