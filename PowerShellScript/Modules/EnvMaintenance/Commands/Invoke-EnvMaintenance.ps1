<#
.SYNOPSIS
    EnvMaintenance 统一入口 —— list / health / repair / update / report。
.DESCRIPTION
    用法:
        .\Invoke-EnvMaintenance.ps1 -Action list
        .\Invoke-EnvMaintenance.ps1 -Action health -Env comfyui
        .\Invoke-EnvMaintenance.ps1 -Action repair -Env comfyui -WhatIf
        .\Invoke-EnvMaintenance.ps1 -Action update -Env comfyui
.PARAMETER Action
    动作：list（清单/环境一览）、health、repair、update、report。
.PARAMETER Env
    目标环境名（health/repair/update 必填，空 = 全部）。
.PARAMETER WhatIf
    透传给 repair/update（预览）。
.PARAMETER Json
    list/health 输出 JSON。
#>
[CmdletBinding()]
param(
    [ValidateSet('list', 'health', 'repair', 'update', 'report')]
    [string]$Action = 'list',
    [string]$Env = '',
    [switch]$WhatIf,
    [switch]$Json,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'

$commandDir = $PSScriptRoot
if (-not $commandDir) { $commandDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$moduleRoot = Split-Path -Parent $commandDir
. (Join-Path $moduleRoot 'Bootstrap.ps1')

function Invoke-EnvMaintenanceChild {
    param([string]$ScriptName, [string[]]$Arguments)

    $scriptPath = Join-Path $PSScriptRoot $ScriptName
    if (-not (Test-Path -LiteralPath $scriptPath)) { throw "命令脚本不存在: $scriptPath" }

    $powerShell = Join-Path $PSHOME 'powershell.exe'
    if (-not (Test-Path -LiteralPath $powerShell)) { $powerShell = 'powershell.exe' }

    $allArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath) + $Arguments
    & $powerShell @allArgs
    return $LASTEXITCODE
}

if ($Action -eq 'list') {
    $manifests = @(Get-EnvManifestNames)
    $envs = @(Get-ProduceEnvironments)

    if ($Json) {
        [pscustomobject]@{
            Manifests = $manifests
            Environments = $envs
        } | ConvertTo-Json -Depth 6
        exit 0
    }

    Write-Host '==== EnvMaintenance 清单 ====' -ForegroundColor Cyan
    foreach ($name in $manifests) {
        Write-Host ("  - {0}" -f $name) -ForegroundColor White
    }
    Write-Host ''
    Write-Host '==== Produce envs.json ====' -ForegroundColor Cyan
    foreach ($env in $envs) {
        Write-Host ("  - {0,-10} {1,-16} venv={2}" -f $env.Id, $env.Name, $env.VenvDir) -ForegroundColor Gray
    }
    exit 0
}

$childArgs = @()
if ($Env) { $childArgs += @('-Env', $Env) }
if ($WhatIf) { $childArgs += '-WhatIf' }
if ($ReportPath) { $childArgs += @('-ReportPath', $ReportPath) }

$exitCode = 0
switch ($Action) {
    'health' {
        if ($Json) { $childArgs += '-Json' }
        $exitCode = Invoke-EnvMaintenanceChild -ScriptName 'Test-EnvHealth.ps1' -Arguments $childArgs
    }
    'repair' {
        $exitCode = Invoke-EnvMaintenanceChild -ScriptName 'Repair-Env.ps1' -Arguments $childArgs
    }
    'update' {
        if ($WhatIf) { $childArgs += '-Check' }
        $exitCode = Invoke-EnvMaintenanceChild -ScriptName 'Update-Env.ps1' -Arguments $childArgs
    }
    'report' {
        $target = $ReportPath
        if (-not $target) { $target = Join-Path (Get-EnvStateRoot) ("report-{0}.md" -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
        $exitCode = Invoke-EnvMaintenanceChild -ScriptName 'Test-EnvHealth.ps1' -Arguments (@($childArgs + @('-ReportPath', $target)))
        Write-EnvInfo '报告' $target
    }
}

exit $exitCode
