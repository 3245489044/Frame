<#
.SYNOPSIS
    【入口】就地更新命名链载荷（施工卡 2-2 · 写侧）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Tools\Chain\Set-SpecChain.ps1。
    缺省 DryRun（零写盘）；-Write 落盘。幂等（签名未变即无操作）。
#>
param(
    [Parameter(Mandatory = $true)][string]$Suite,

    [Parameter(Mandatory = $true)][string]$Chain,

    [string]$From = '',

    [string]$Json = '',

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\Tools\Chain\Set-SpecChain.ps1'

$params = @{ Suite = $Suite; Chain = $Chain }
if ($PSBoundParameters.ContainsKey('From')) { $params['From'] = $From }
if ($PSBoundParameters.ContainsKey('Json')) { $params['Json'] = $Json }
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
