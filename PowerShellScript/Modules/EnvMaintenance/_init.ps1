# @requires: -
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance
# @exports: Import-EnvMaintenance

<#
.SYNOPSIS
    EnvMaintenance 一键初始化（直接运行或点引均可）。
.DESCRIPTION
    加载 Core 与 Provider，然后列出可用命令。
    用法:
        . .\Modules\EnvMaintenance\_init.ps1
        .\Modules\EnvMaintenance\Commands\Test-EnvHealth.ps1 -Env comfyui
.NOTES
    依赖：无（仅 Windows PowerShell 5.1 内置 cmdlet）。
#>

param([string]$ProjectRoot = '')

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir 'Bootstrap.ps1')

Write-Host ''
Write-Host '==================================================' -ForegroundColor Cyan
Write-Host '   EnvMaintenance v1.0.0 — 环境体检/修复/升级' -ForegroundColor Cyan
Write-Host '==================================================' -ForegroundColor Cyan
Write-Host ''
Write-EnvInfo '模块根' $global:EnvMaintenanceRoot

try {
    Write-EnvInfo 'Produce 配置' (Get-ProduceConfigRoot)
} catch {
    Write-EnvWarn "Produce 配置根未找到: $($_.Exception.Message)"
}

Write-Host ''
Write-Host '  可用命令:' -ForegroundColor Yellow
Write-Host '    Test-EnvHealth.ps1 -Env comfyui     只读体检（含 verify 断言）' -ForegroundColor White
Write-Host '    Repair-Env.ps1    -Env comfyui     补齐缺失/修复版本' -ForegroundColor White
Write-Host '    Update-Env.ps1    -Env comfyui     按清单升级（-Check 仅预览）' -ForegroundColor White
Write-Host '    Invoke-EnvMaintenance.ps1 -Action list|health|repair|update' -ForegroundColor White
Write-Host ''
