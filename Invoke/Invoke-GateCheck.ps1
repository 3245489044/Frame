<#
.SYNOPSIS
    【入口】门禁聚合检查
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Frame\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1
    一条命令跑齐 13 件 DSL 门禁检查并输出 PASS/FAIL 表。
.PARAMETER All
    跑全部门禁（默认）。
.PARAMETER Only
    只跑指定门禁（如 AgentBuildPoint,ClinerulesReferences）。
.PARAMETER Manifest
    门禁清单文件路径，默认同目录 GateManifests.psd1。
.PARAMETER Parallel
    并发上限，默认 1（串行）。
.EXAMPLE
    .\Invoke-GateCheck.ps1 -All
.EXAMPLE
    .\Invoke-GateCheck.ps1 -Only AgentBuildPoint,DataContracts
#>
param(
    [switch]$All,
    [string[]]$Only = @(),
    [string]$Manifest = ''
)

$target = Join-Path $PSScriptRoot "..\PowerShellScript\Operation\DSL\Invoke-GateCheck.ps1"

$params = @{}
if ($All)      { $params['All'] = $true }
if ($PSBoundParameters.ContainsKey('Only') -and $Only.Count -gt 0) { $params['Only'] = $Only }
if ($PSBoundParameters.ContainsKey('Manifest')) { $params['Manifest'] = $Manifest }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
