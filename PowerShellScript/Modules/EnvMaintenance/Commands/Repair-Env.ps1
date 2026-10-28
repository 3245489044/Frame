<#
.SYNOPSIS
    环境修复 —— 按清单补齐缺失/版本不符的依赖（plan → apply → verify → rollback）。
.DESCRIPTION
    用法:
        .\Repair-Env.ps1 -Env comfyui -WhatIf
        .\Repair-Env.ps1 -Env comfyui
        .\Repair-Env.ps1 -Env comfyui -Phase 1
        .\Repair-Env.ps1 -Env comfyui -Name torch
.PARAMETER Env
    清单/环境名（对应 Manifests\<Env>.json）。
.PARAMETER Manifest
    显式清单路径或文件名。
.PARAMETER Phase
    仅处理指定 phase（0 = 全部）。
.PARAMETER Name
    仅处理单项。
.PARAMETER Force
    强制重装所选范围（谨慎使用）。
.PARAMETER WhatIf
    只输出计划，不执行。
.PARAMETER NoRollback
    复核失败时不回滚 pip 依赖快照。
.PARAMETER SkipVerify
    跳过复核（不推荐）。
.PARAMETER ReportPath
    报告落盘路径。
#>
[CmdletBinding()]
param(
    [string]$Env = '',
    [string]$Manifest = '',
    [int]$Phase = 0,
    [string]$Name = '',
    [switch]$Force,
    [switch]$WhatIf,
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

$allOk = $true
foreach ($target in $targets) {
    Write-Host ''
    Write-Host ("==== 修复环境: {0} ====" -f $target.envId) -ForegroundColor Cyan
    Write-EnvInfo '清单' $target.manifestPath

    $plan = @(New-EnvPlan -Manifest $target -Phase $Phase -Name $Name -Force:$Force)
    Write-EnvPlanTable -Plan $plan

    $report = Invoke-EnvPlan -Manifest $target -Phase $Phase -Name $Name -Force:$Force `
        -WhatIf:$WhatIf -NoRollback:$NoRollback -SkipVerify:$SkipVerify

    if ($WhatIf) {
        Write-EnvWarn '仅预览（-WhatIf），未执行安装'
    } else {
        Write-EnvApplyTable -Report $report
    }

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
