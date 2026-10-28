# @requires: -
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-console
# @exports: Write-EnvStep, Write-EnvOk, Write-EnvWarn, Write-EnvFail, Write-EnvInfo

<#
.SYNOPSIS
    EnvMaintenance 统一控制台输出（不依赖 Core\OutputHelpers，便于脚本独立运行）。
#>

function Write-EnvStep {
    param([string]$Message)
    Write-Host "  == $Message" -ForegroundColor Cyan
}

function Write-EnvOk {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-EnvWarn {
    param([string]$Message)
    Write-Host "  [!!] $Message" -ForegroundColor Yellow
}

function Write-EnvFail {
    param([string]$Message)
    Write-Host "  [XX] $Message" -ForegroundColor Red
}

function Write-EnvInfo {
    param([string]$Label, [string]$Value)
    Write-Host ("  {0,-18} {1}" -f ($Label + ":"), $Value) -ForegroundColor Gray
}
