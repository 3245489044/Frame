# @requires: Core/EnvPath
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-manifest
# @exports: Get-EnvManifestPath, Get-EnvManifestNames, Load-EnvManifest, Resolve-EnvManifestSelection, Get-EnvManifestItemOrder, Get-EnvManifestVenv, Get-EnvManifestPython

<#
.SYNOPSIS
    维护清单加载 —— 清单 v2（在 Install manifest 基础上增加 version/index/verify/dependsOn）。
.DESCRIPTION
    清单示例字段：
        envId / stateDir / python.venv / python.executable / pip.index / pip.timeoutSeconds
        items[]: name type phase venv source packages version requirements index verify dependsOn
#>

function Get-EnvManifestPath {
    param([Parameter(Mandatory = $true)][string]$Name)

    $fileName = $Name
    if (-not $fileName.EndsWith('.json')) { $fileName = $fileName + '.json' }

    $path = Join-Path (Join-Path (Get-EnvMaintenanceRoot) 'Manifests') $fileName
    if (-not (Test-Path -LiteralPath $path)) { throw "维护清单不存在: $path" }
    return $path
}

function Get-EnvManifestNames {
    $dir = Join-Path (Get-EnvMaintenanceRoot) 'Manifests'
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Filter '*.json' | ForEach-Object { $_.Name })
}

function Load-EnvManifest {
    <#
    .SYNOPSIS
        加载并归一化维护清单。
    .PARAMETER Name
        清单名（位于 Manifests\，可省略 .json）。
    .PARAMETER Path
        清单绝对路径（优先于 Name）。
    #>
    param([string]$Name = '', [string]$Path = '')

    $manifestPath = $Path
    if (-not $manifestPath) { $manifestPath = Get-EnvManifestPath -Name $Name }
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "维护清单不存在: $manifestPath" }

    $manifest = Read-JsonConfig -Path $manifestPath

    if (-not $manifest.envId) {
        $manifest | Add-Member -NotePropertyName 'envId' -NotePropertyValue ([System.IO.Path]::GetFileNameWithoutExtension($manifestPath)) -Force
    }
    if (-not $manifest.python) {
        $manifest | Add-Member -NotePropertyName 'python' -NotePropertyValue ([pscustomobject]@{ executable = 'python'; venv = '' }) -Force
    }
    if (-not $manifest.pip) {
        $manifest | Add-Member -NotePropertyName 'pip' -NotePropertyValue ([pscustomobject]@{ index = ''; timeoutSeconds = 900 }) -Force
    }

    $manifest | Add-Member -NotePropertyName 'manifestPath' -NotePropertyValue $manifestPath -Force

    $items = @()
    foreach ($item in @($manifest.items)) {
        if (-not $item -or -not $item.name) { continue }

        if (-not $item.type) { $item | Add-Member -NotePropertyName 'type' -NotePropertyValue 'pip-venv' -Force }
        if (-not $item.phase) { $item | Add-Member -NotePropertyName 'phase' -NotePropertyValue 1 -Force }
        if (-not $item.venv -and $manifest.python.venv) {
            $item | Add-Member -NotePropertyName 'venv' -NotePropertyValue ([string]$manifest.python.venv) -Force
        }
        if (-not $item.index -and $manifest.pip.index) {
            $item | Add-Member -NotePropertyName 'index' -NotePropertyValue ([string]$manifest.pip.index) -Force
        }
        if (-not $item.timeoutSeconds -and $manifest.pip.timeoutSeconds) {
            $item | Add-Member -NotePropertyName 'timeoutSeconds' -NotePropertyValue ([int]$manifest.pip.timeoutSeconds) -Force
        }
        if (-not $item.cache) {
            $item | Add-Member -NotePropertyName 'cache' -NotePropertyValue $false -Force
        }

        $items += $item
    }
    $manifest | Add-Member -NotePropertyName 'items' -NotePropertyValue $items -Force

    return $manifest
}

function Resolve-EnvManifestSelection {
    <#
    .SYNOPSIS
        按 Env / Manifest 参数解析待处理的清单集合。
    #>
    param([string]$Env = '', [string]$Manifest = '')

    if ($Manifest) {
        if (Test-Path -LiteralPath $Manifest) { return @(Load-EnvManifest -Path (Resolve-Path -LiteralPath $Manifest).ProviderPath) }
        return @(Load-EnvManifest -Name $Manifest)
    }

    if ($Env) {
        return @(Load-EnvManifest -Name $Env)
    }

    $manifests = @()
    foreach ($name in (Get-EnvManifestNames)) {
        $manifests += (Load-EnvManifest -Name $name)
    }
    return $manifests
}

function Get-EnvManifestVenv {
    <#
    .SYNOPSIS
        清单的 venv 根：python.venv 优先，否则取第一个声明 venv 的 item。
    #>
    param([Parameter(Mandatory = $true)]$Manifest)

    if ($Manifest.python -and $Manifest.python.venv) { return [string]$Manifest.python.venv }
    foreach ($item in @($Manifest.items)) {
        if ($item.venv) { return [string]$item.venv }
    }
    return ''
}

function Get-EnvManifestPython {
    <#
    .SYNOPSIS
        清单内实际使用的解释器：已存在 venv 时用 venv python，否则用基础解释器创建 venv。
    #>
    param([Parameter(Mandatory = $true)]$Manifest)

    $venv = Get-EnvManifestVenv -Manifest $Manifest
    if ($venv) {
        $python = Resolve-VenvPython -VenvDir $venv
        if (Test-Path -LiteralPath $python) { return $python }
    }

    $base = 'python'
    if ($Manifest.python -and $Manifest.python.executable) { $base = [string]$Manifest.python.executable }
    return $base
}

function Get-EnvManifestItemOrder {
    <#
    .SYNOPSIS
        按 dependsOn 拓扑排序（清单默认顺序作为平局顺序）；检测循环依赖。
    #>
    param([Parameter(Mandatory = $true)]$Manifest)

    $ordered = @()
    $remaining = @($Manifest.items)
    $progress = $true

    while ($remaining.Count -gt 0 -and $progress) {
        $progress = $false
        $next = @()
        foreach ($item in $remaining) {
            $deps = @()
            if ($item.dependsOn) { $deps = @($item.dependsOn) }

            $ready = $true
            foreach ($dep in $deps) {
                $found = $false
                foreach ($done in $ordered) { if ($done.name -eq $dep) { $found = $true; break } }
                if (-not $found) { $ready = $false; break }
            }

            if ($ready) {
                $ordered += $item
                $progress = $true
            } else {
                $next += $item
            }
        }
        $remaining = $next
    }

    if ($remaining.Count -gt 0) {
        $names = ($remaining | ForEach-Object { $_.name }) -join ', '
        throw "维护清单存在循环依赖或缺失依赖: $names"
    }

    return $ordered
}
