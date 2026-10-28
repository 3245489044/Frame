# @requires: -
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-path
# @exports: Get-EnvMaintenanceRoot, Get-ProduceConfigRoot, Get-RepoRoot, Read-JsonConfig, Resolve-VenvPython, Get-VenvDirFromPython, Expand-EnvPath, Get-EnvStateRoot, Get-EnvLogRoot

<#
.SYNOPSIS
    路径与 JSON 读取 —— EnvMaintenance 唯一的位置真源。
.DESCRIPTION
    向上回溯定位 Produce 配置根（envs.json 所在），并提供 venv/python 路径互推。
    默认状态与日志目录为 D:\DevEnv\_state\EnvMaintenance（可被清单 stateDir 覆盖）。
#>

function Get-EnvMaintenanceRoot {
    if ($global:EnvMaintenanceRoot -and (Test-Path -LiteralPath $global:EnvMaintenanceRoot)) {
        return $global:EnvMaintenanceRoot
    }
    return $PSScriptRoot
}

function Get-ProduceConfigRoot {
    <#
    .SYNOPSIS
        定位 Produce 配置根（含 workflows 子目录与 envs.json）。
    #>
    param([string]$StartDir = "")

    if ($global:EnvProduceConfigRoot -and (Test-Path -LiteralPath $global:EnvProduceConfigRoot)) {
        return $global:EnvProduceConfigRoot
    }

    $dir = $StartDir
    if (-not $dir) { $dir = Get-EnvMaintenanceRoot }

    while ($dir) {
        foreach ($relative in @('Projects\Plug\Produce\Config', 'Projects\Produce\Config', 'Config')) {
            $candidate = Join-Path $dir $relative
            if (Test-Path -LiteralPath (Join-Path $candidate 'workflows')) {
                $global:EnvProduceConfigRoot = $candidate
                return $candidate
            }
        }
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { break }
        $dir = $parent
    }

    throw "未能定位 Produce 配置根（含 workflows\ 与 envs.json）；起点: $StartDir"
}

function Get-RepoRoot {
    <#
    .SYNOPSIS
        仓库根（同时包含 Projects 与 Frame 的目录）。
    #>
    $dir = Get-EnvMaintenanceRoot
    while ($dir) {
        if ((Test-Path -LiteralPath (Join-Path $dir 'Projects')) -and (Test-Path -LiteralPath (Join-Path $dir 'Frame'))) {
            return $dir
        }
        $parent = Split-Path -Parent $dir
        if (-not $parent -or $parent -eq $dir) { break }
        $dir = $parent
    }
    return $(Split-Path -Parent (Split-Path -Parent (Get-ProduceConfigRoot)))
}

function Read-JsonConfig {
    <#
    .SYNOPSIS
        读取允许 `//` 整行注释与尾逗号的 JSON（PS 5.1 的 ConvertFrom-Json 不支持注释）。
    .PARAMETER Path
        文件绝对路径。
    #>
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { throw "JSON 配置不存在: $Path" }

    $raw = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $lines = $raw -split "`r?`n"
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line.TrimStart().StartsWith('//')) { continue }
        [void]$kept.Add($line)
    }

    $text = ($kept -join "`n")
    # 去尾逗号（仅 ] 或 } 之前的逗号）
    $text = [regex]::Replace($text, ',(\s*[\]}])', '$1')
    return ($text | ConvertFrom-Json)
}

function Expand-EnvPath {
    param([string]$Value)
    if (-not $Value) { return $Value }
    return [System.Environment]::ExpandEnvironmentVariables($Value)
}

function Resolve-VenvPython {
    param([Parameter(Mandatory = $true)][string]$VenvDir)
    return (Join-Path $VenvDir 'Scripts\python.exe')
}

function Get-VenvDirFromPython {
    <#
    .SYNOPSIS
        由 python.exe 路径反推 venv 根（`...\.venv\Scripts\python.exe` → `...\.venv`）。
    #>
    param([string]$PythonPath)

    if (-not $PythonPath) { return $null }
    $scripts = Split-Path -Parent $PythonPath
    if ((Split-Path -Leaf $scripts) -ieq 'Scripts') {
        return (Split-Path -Parent $scripts)
    }
    return (Split-Path -Parent $PythonPath)
}

function Get-EnvStateRoot {
    <#
    .SYNOPSIS
        状态根目录（快照/标记/报告）；清单未声明 stateDir 时落在 D:\DevEnv\_state\EnvMaintenance。
    #>
    param([object]$Manifest = $null)

    $root = ''
    if ($Manifest -and $Manifest.stateDir) { $root = [string]$Manifest.stateDir }
    if (-not $root) { $root = (Join-Path 'D:\DevEnv' '_state\EnvMaintenance') }
    if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Force -Path $root | Out-Null }
    return $root
}

function Get-EnvLogRoot {
    param([object]$Manifest = $null)

    $root = Join-Path (Get-EnvStateRoot -Manifest $Manifest) 'logs'
    if (-not (Test-Path -LiteralPath $root)) { New-Item -ItemType Directory -Force -Path $root | Out-Null }
    return $root
}
