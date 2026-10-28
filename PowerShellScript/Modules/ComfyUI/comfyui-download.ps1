<#
.SYNOPSIS
    ComfyUI 漫画套件安装/维护入口（薄壳 — 已收敛到 EnvMaintenance）。
.DESCRIPTION
    历史脚本内的 Provider/探测/执行逻辑已上收到 Modules\EnvMaintenance：
    本脚本只做参数转译，调用 Repair-Env.ps1（清单 = EnvMaintenance\Manifests\comfyui.json）。

    用法:
      .\comfyui-download.ps1                 # 按清单补齐缺失项
      .\comfyui-download.ps1 -Phase 1        # 只处理 phase 1（环境/框架）
      .\comfyui-download.ps1 -Name torch     # 只处理单项
      .\comfyui-download.ps1 -WhatIf         # 只预览计划
      .\comfyui-download.ps1 -Force          # 强制重装所选范围
.PARAMETER Manifest
    维护清单路径或文件名（默认 comfyui.json）。
.PARAMETER Phase
    只执行某个 phase（默认全部）。
.PARAMETER Name
    只处理单个 item。
.PARAMETER Force
    强制重装（跳过已安装判定）。
.PARAMETER WhatIf
    只预览，不实际执行。
#>
[CmdletBinding()]
param(
    [string]$Manifest = '',
    [int]$Phase = 0,
    [string]$Name = '',
    [switch]$Force,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repairScript = Join-Path $scriptDir '..\EnvMaintenance\Commands\Repair-Env.ps1'

if (-not (Test-Path -LiteralPath $repairScript)) {
    Write-Host "[XX] 未找到 EnvMaintenance: $repairScript" -ForegroundColor Red
    exit 2
}

$arguments = @('-Env', 'comfyui')
if ($Manifest) { $arguments += @('-Manifest', $Manifest) }
if ($Phase -gt 0) { $arguments += @('-Phase', [string]$Phase) }
if ($Name) { $arguments += @('-Name', $Name) }
if ($Force) { $arguments += '-Force' }
if ($WhatIf) { $arguments += '-WhatIf' }

$powerShell = Join-Path $PSHOME 'powershell.exe'
if (-not (Test-Path -LiteralPath $powerShell)) { $powerShell = 'powershell.exe' }

& $powerShell -NoProfile -ExecutionPolicy Bypass -File $repairScript @arguments
exit $LASTEXITCODE
