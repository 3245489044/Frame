<#
.SYNOPSIS
    【入口】批量抽链（压回 S2 折叠膨胀；对含同一内联载荷 ≥2 次的套件一次处理）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Tools\Chain\New-SpecChains.ps1。
    缺省 DryRun（零写盘：只列出将处理的套件）；-Write 落盘。
.PARAMETER CasesRoot
    用例根（缺省 = AgentTest）。
.PARAMETER MinUses
    抽链的最小引用次数（缺省 2）。
.PARAMETER Write
    落盘开关（缺省 DryRun）。
.EXAMPLE
    .\New-SpecChains.ps1
.EXAMPLE
    .\New-SpecChains.ps1 -Write
#>
param(
    [string]$CasesRoot = '',

    [int]$MinUses = 2,

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\Tools\Chain\New-SpecChains.ps1'

$params = @{ MinUses = $MinUses }
if ($PSBoundParameters.ContainsKey('CasesRoot')) { $params['CasesRoot'] = $CasesRoot }
if ($PSBoundParameters.ContainsKey('Write')) { $params['Write'] = $true }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
