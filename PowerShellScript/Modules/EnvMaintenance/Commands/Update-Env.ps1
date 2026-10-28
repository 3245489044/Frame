<#
.SYNOPSIS
    环境升级 —— 对照清单版本，升级 Outdated 项（-Check 只出差异）。
.DESCRIPTION
    用法:
        .\Update-Env.ps1 -Env comfyui -Check
        .\Update-Env.ps1 -Env comfyui
        .\Update-Env.ps1 -Env comfyui -NoRollback
.PARAMETER Check
    只输出计划差异，不执行（等价 -WhatIf）。
#>
[CmdletBinding()]
param(
    [string]$Env = '',
    [string]$Manifest = '',
    [switch]$Check,
    [switch]$Force,
    [switch]$NoRollback,
    [switch]$SkipVerify,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'

$commandDir = $PSScriptRoot
if (-not $commandDir) { $commandDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$moduleRoot = Split-Path -Parent $commandDir
. (Join-Path $moduleRoot 'Bootstrap.ps1')

$targets = @(Resolve-EnvManifestSelection -Env $Env -Manifest $Manifest)
if ($targets.Count -eq 0) {
    Write-EnvFail '未找到任何维护清单'
    exit 2
}

$dryRun = [bool]$Check
$allOk = $true

foreach ($target in $targets) {
    Write-Host ''
    Write-Host ("==== 升级环境: {0} ====" -f $target.envId) -ForegroundColor Cyan
    Write-EnvInfo '清单' $target.manifestPath

    $plan = @(New-EnvPlan -Manifest $target -Force:$Force)
    $pending = @($plan | Where-Object { $_.Action -eq 'Apply' })
    Write-EnvPlanTable -Plan $plan

    if ($dryRun) {
        if ($pending.Count -eq 0) { Write-EnvOk '已是最新，无需升级' }
        else { Write-EnvWarn ("待升级 " + $pending.Count + " 项（-Check 仅预览）") }
        if ($pending.Count -gt 0) { $allOk = $false }
        continue
    }

    if ($pending.Count -eq 0) {
        Write-EnvOk '已是最新，无需升级'
        continue
    }

    $report = Invoke-EnvPlan -Manifest $target -Force:$Force -NoRollback:$NoRollback -SkipVerify:$SkipVerify
    Write-EnvApplyTable -Report $report
    if (-not $report.Ok) { $allOk = $false }

    if ($ReportPath -and $targets.Count -eq 1) {
        $format = 'Json'
        if ($ReportPath.ToLower().EndsWith('.md')) { $format = 'Markdown' }
        $export = [pscustomobject]@{
            EnvId = $report.EnvId; ManifestPath = $report.ManifestPath; CheckedAt = $report.GeneratedAt
            Ok = $report.Ok; Items = @($report.Verifications | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Type = ''; Ok = $_.Ok; Version = $_.Version; Reason = $_.Reason }
            })
        }
        Export-EnvReport -Report $export -Path $ReportPath -Format $format | Out-Null
        Write-EnvInfo '报告' $ReportPath
    }
}

if ($allOk) { exit 0 } else { exit 1 }
