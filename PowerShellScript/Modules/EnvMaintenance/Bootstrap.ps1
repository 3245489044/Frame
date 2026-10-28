# @requires: -
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-bootstrap
# @exports: Import-EnvMaintenance

<#
.SYNOPSIS
    EnvMaintenance 引导器 —— 按固定顺序点引所有 Core 与 Provider 文件。
.DESCRIPTION
    本文件被 _init.ps1 与各 Commands 脚本点引，使脚本既能经 PScriptManager 以函数方式加载，
    也能直接 `powershell -File xxx.ps1` 运行（无外部模块依赖）。
.NOTES
    依赖：无（仅内置 cmdlet）；兼容 Windows PowerShell 5.1。
#>

$global:EnvMaintenanceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$envMaintenanceCore = @(
    'Core\Console.ps1',
    'Core\EnvPath.ps1',
    'Core\NativeRunner.ps1',
    'Core\PipRunner.ps1',
    'Core\PythonVerifier.ps1',
    'Core\EnvironmentDiscovery.ps1',
    'Core\EnvManifest.ps1',
    'Core\EnvProviders.ps1',
    'Core\EnvPlan.ps1',
    'Core\EnvReport.ps1'
)

foreach ($relative in $envMaintenanceCore) {
    $path = Join-Path $global:EnvMaintenanceRoot $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "EnvMaintenance 核心文件缺失: $path" }
    . $path
}

$envMaintenanceProviders = @(
    'Providers\VenvProvider.ps1',
    'Providers\PipVenvProvider.ps1',
    'Providers\GitCloneProvider.ps1',
    'Providers\HttpFileProvider.ps1',
    'Providers\PythonVerifyProvider.ps1'
)

foreach ($relative in $envMaintenanceProviders) {
    $path = Join-Path $global:EnvMaintenanceRoot $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "EnvMaintenance Provider 缺失: $path" }
    . $path
}

function Import-EnvMaintenance {
    <#
    .SYNOPSIS
        兼容入口：引导已完成，本函数仅用于声明式加载。
    #>
    param([switch]$Quiet)
    if (-not $Quiet) { Write-EnvInfo '模块根' $global:EnvMaintenanceRoot }
}
