<#
.SYNOPSIS
    启动冒烟门禁（Gate=LaunchSmoke）—— GUI 存活断言（唯一实现委托 Share\Launch\Invoke-LaunchSmoke.ps1）。

.DESCRIPTION
    本件只做「基线 → 参数 → 委托」，不复制判据（SingleImplementation）：
      macro 判据「能启动」= GUI 存活 AliveSeconds ∧ 未新增 bootstrap 失败/函数库降级诊断。
    退出码：0 pass / 1 fail / 2 前置缺失（报告模式不设退出码）。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [string]$Product = ''
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name LaunchSmoke
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

if ([string]::IsNullOrWhiteSpace($Product)) { $Product = [string]$baseline.DefaultProduct.Value }

$impl = Join-Path $repoRoot 'Frame\PowerShellScript\Share\Launch\Invoke-LaunchSmoke.ps1'
if (-not (Test-Path -LiteralPath $impl)) {
    Write-Host ("  [FAIL] 冒烟实现缺失: {0}" -f $impl) -ForegroundColor Red
    exit 2
}

& $impl -Product $Product -AliveSeconds ([int]$baseline.AliveSeconds.Value)
$rc = if ($null -eq $LASTEXITCODE) { 2 } else { [int]$LASTEXITCODE }

if ($rc -ne 0 -and -not $Check) { exit 0 }
exit $rc
