<#
.SYNOPSIS
    环境体检 —— 只读检查（不安装、不写标记）。
.DESCRIPTION
    逐项执行 Test + verify 断言（例如 `import comfy_kitchen`、torch CUDA 版本），
    同时校验 Produce/Config/envs.json 中是否存在同名环境声明。
    用法:
        .\Test-EnvHealth.ps1 -Env comfyui
        .\Test-EnvHealth.ps1 -All -Json
.PARAMETER Env
    清单/环境名（如 comfyui；对应 Manifests\comfyui.json）。
.PARAMETER Manifest
    显式清单路径或文件名。
.PARAMETER Json
    以 JSON 输出（便于脚本消费）。
.PARAMETER ReportPath
    报告落盘路径（.md 或 .json 由扩展名决定）。
#>
[CmdletBinding()]
param(
    [string]$Env = '',
    [string]$Manifest = '',
    [switch]$All,
    [switch]$Json,
    [string]$ReportPath = ''
)

$ErrorActionPreference = 'Stop'

$commandDir = $PSScriptRoot
if (-not $commandDir) { $commandDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$moduleRoot = Split-Path -Parent $commandDir
. (Join-Path $moduleRoot 'Bootstrap.ps1')

if ($All) { $Env = ''; $Manifest = '' }
$targets = @(Resolve-EnvManifestSelection -Env $Env -Manifest $Manifest)
if ($targets.Count -eq 0) {
    Write-EnvFail '未找到任何维护清单'
    exit 2
}

$results = @()
$allOk = $true

foreach ($target in $targets) {
    $health = Test-EnvManifestHealth -Manifest $target

    try {
        $produceEnv = Get-ProduceEnvironment -Id $health.EnvId
        if (-not $produceEnv) {
            $health.Ok = $false
            $allOk = $false
            Write-EnvWarn "envs.json 未声明环境 $($health.EnvId)（C# EnvHost 不会管理它）"
        } elseif ($produceEnv.VenvDir -and $health.Venv -and ($produceEnv.VenvDir -ne $health.Venv)) {
            $health.Ok = $false
            $allOk = $false
            Write-EnvWarn "venv 路径与 envs.json 不一致: 清单=$($health.Venv) / envs.json=$($produceEnv.VenvDir)"
        }
    } catch {
        Write-EnvWarn "跳过 envs.json 校验: $($_.Exception.Message)"
    }

    if (-not $Json) { Write-EnvHealthTable -Health $health }
    if (-not $health.Ok) { $allOk = $false }
    $results += $health
}

if ($ReportPath) {
    $format = 'Json'
    if ($ReportPath.ToLower().EndsWith('.md')) { $format = 'Markdown' }
    $report = $results[0]
    if ($results.Count -gt 1) {
        $report = [pscustomobject]@{
            EnvId = 'all'; CheckedAt = (Get-Date); ManifestPath = ''
            Ok = $allOk; Items = @($results | ForEach-Object { $_.Items } | ForEach-Object { $_ })
        }
    }
    Export-EnvReport -Report $report -Path $ReportPath -Format $format | Out-Null
    Write-EnvInfo '报告' $ReportPath
}

if ($Json) { $results | ConvertTo-Json -Depth 8 }

if ($allOk) { exit 0 } else { exit 1 }
