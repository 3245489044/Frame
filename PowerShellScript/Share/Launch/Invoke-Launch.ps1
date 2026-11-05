<#
.SYNOPSIS
    兼容壳（薄转发）—— 旧 `-Action launch` 开关形态 → 通用执行体 Invoke-LaunchEntry.ps1。

.DESCRIPTION
    唯一实现 = Share\Launch\Invoke-LaunchEntry.ps1（契约驱动 S0–S7）。
    本件只做「开关 → 契约化 argv」映射，零判定/零产品知识；保留旧调用面以支持人工诊断与回退。
    新代码请直接调 Invoke-LaunchEntry.ps1（或 Invoke-Frame -Action launch -Product <p> -Entry <id>）。
#>
[CmdletBinding()]
param(
    [switch]$Interactive,
    [switch]$NoBuild,
    [switch]$Smoke,
    [string]$Configuration = 'Debug',
    [switch]$ForceBuild,
    [switch]$BuildOnly,
    [switch]$GateOnly,
    [switch]$Diagnose,
    [switch]$KillHost,
    [switch]$KillAnyPath,
    [ValidateSet('off', 'load', 'stale')]
    [string]$ContractPreflight = 'off',
    [string]$Product = 'filedriven',
    [string]$Entry = ''
)

$ErrorActionPreference = 'Stop'

$argv = [System.Collections.Generic.List[string]]::new()
$argv.Add('--configuration'); $argv.Add($Configuration)
if ($Interactive) { $argv.Add('--interactive') }
if ($NoBuild) { $argv.Add('--no-build') }
if ($Smoke) { $argv.Add('--smoke') }
if ($ForceBuild) { $argv.Add('--fast') }
if ($BuildOnly) { $argv.Add('--build-only') }
if ($GateOnly) { $argv.Add('--gate-only') }
if ($Diagnose) { $argv.Add('--diagnose') }
if ($KillHost) { $argv.Add('--kill-host') }
if ($KillAnyPath) { $argv.Add('--any-instance') }
if ($ContractPreflight -ne 'off') { $argv.Add('--contract-preflight'); $argv.Add($ContractPreflight) }

$entryScript = Join-Path $PSScriptRoot 'Invoke-LaunchEntry.ps1'
$invoke = @{ Product = $Product; RawArgs = @($argv) }
if (-not [string]::IsNullOrWhiteSpace($Entry)) { $invoke['Entry'] = $Entry }
& $entryScript @invoke
exit $LASTEXITCODE
