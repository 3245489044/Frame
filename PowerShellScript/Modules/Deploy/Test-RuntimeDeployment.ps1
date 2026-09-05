<#
.SYNOPSIS
    通用运行目录部署校验 — 校验各产品闭包是否已正确部署到 FileDriven 运行目录。

.DESCRIPTION
    配合全局自投放框架（仓库根 Directory.Build.targets 的 DeploySelfToRuntime + L2 通用装载器）。
    校验 FileDriven\BuildOutput\<Configuration>\ 下是否具备某产品（Agent / Engine / Produce / PrjBridge）
    的关键 DLL 与装配清单（PrjBridge.deps.json），并核对清单 runtime 资产是否有磁盘缺失。

    与 C# 侧 ExtensionAssemblyHost.ValidateDeployment() 语义一致，供运维脚本在部署后快速自检。

.PARAMETER Products
    要校验的产品列表，逗号分隔。合法值：Agent, Engine, Produce, PrjBridge, All（默认 All）。

.PARAMETER Configuration
    构建配置，默认 Debug。

.EXAMPLE
    . "Modules\Deploy\Test-RuntimeDeployment.ps1" -Products All

.NOTES
    单真源：运行目录经 Get-ProjectRoot + ReadProjectConfig 解析，不散落硬编码路径。
#>
# @requires Get-ProjectRoot, Get-FileDrivenDir

[CmdletBinding()]
param(
    [ValidateSet("Agent", "Engine", "Produce", "PrjBridge", "All")]
    [string]$Products = "All",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\Core\ReadProjectConfig.ps1")
$scriptRoot = Get-ProjectRoot

$fileDrivenDir = Get-FileDrivenDir -ProjectRoot $scriptRoot
$runtimeDir = Join-Path $fileDrivenDir "BuildOutput\$Configuration"

function Test-RuntimeDeployment {
    <#
    .SYNOPSIS
        校验运行目录中某产品的关键 DLL 与装配清单是否齐备。

    .OUTPUTS
        PSCustomObject { Product, RequiredFiles, MissingFiles, DepsOk }
    #>
    param(
        [string]$Product,
        [string]$RuntimeDir
    )

    # 各产品在运行目录应具备的关键 DLL（组合根 + 代表性核心模块）
    $requiredByProduct = @{
        "PrjBridge" = @("PrjBridge.dll", "PrjBridge.deps.json", "PrjBridge.runtimeconfig.json")
        "Agent"     = @("Agent.Launcher.dll", "Agent.Core.dll", "Agent.Modules.dll")
        "Engine"    = @("GameEditor.Host.dll", "GameEditor.Scene.dll", "GameEditor.Graphics.dll")
        "Produce"   = @("Produce.Launcher.dll", "Produce.Functions.dll", "Produce.ComfyBridge.dll")
    }

    $required = $requiredByProduct[$Product]
    $missing = @()

    foreach ($rel in $required) {
        if (-not (Test-Path (Join-Path $RuntimeDir $rel))) {
            $missing += $rel
        }
    }

    # PrjBridge 装配清单一致性：清单 runtime 资产是否有磁盘缺失（对齐 ValidateDeployment）
    $depsOk = $true
    if ($Product -eq "PrjBridge" -and -not $missing) {
        $depsPath = Join-Path $RuntimeDir "PrjBridge.deps.json"
        if (Test-Path $depsPath) {
            try {
                $deps = Get-Content $depsPath -Raw | ConvertFrom-Json
                foreach ($lib in $deps.libraries.PSObject.Properties) {
                    if (-not $lib.Value.runtime) { continue }
                    foreach ($asset in $lib.Value.runtime.PSObject.Properties.Name) {
                        if ($asset -notlike "*.dll") { continue }
                        if (-not (Test-Path (Join-Path $RuntimeDir $asset))) {
                            $depsOk = $false
                            $missing += "deps:$asset"
                        }
                    }
                }
            } catch {
                $depsOk = $false
                $missing += "deps:parse-error"
            }
        } else {
            $depsOk = $false
        }
    }

    [PSCustomObject]@{
        Product      = $Product
        RequiredFiles = ($required -join ", ")
        MissingFiles = ($missing -join ", ")
        DepsOk       = $depsOk
        Ready        = ($missing.Count -eq 0)
    }
}

# 解析产品集合
$selected = if ($Products -eq "All") { @("PrjBridge", "Agent", "Engine", "Produce") } else { $Products -split "," | ForEach-Object { $_.Trim() } }

if (-not (Test-Path $runtimeDir)) {
    Write-Warning "运行目录不存在: $runtimeDir"
    exit 1
}

Write-Host ""
Write-Host "[运行目录部署校验] $runtimeDir" -ForegroundColor Cyan
Write-Host ""

$allReady = $true
foreach ($product in $selected) {
    $result = Test-RuntimeDeployment -Product $product -RuntimeDir $runtimeDir
    $status = if ($result.Ready) { "[OK]  " } else { "[缺失]" }
    $color   = if ($result.Ready) { "Green" } else { "Red" }
    Write-Host ("{0} {1,-10} 必需: {2}" -f $status, $result.Product, $result.RequiredFiles) -ForegroundColor $color
    if (-not $result.Ready) {
        $allReady = $false
        Write-Host ("       缺失: {0}" -f $result.MissingFiles) -ForegroundColor Yellow
    }
    if ($result.Product -eq "PrjBridge") {
        $depsStatus = if ($result.DepsOk) { "清单一致" } else { "清单缺失/不一致" }
        Write-Host ("       装配清单: {0}" -f $depsStatus) -ForegroundColor $(if ($result.DepsOk) { "DarkGray" } else { "Yellow" })
    }
}

Write-Host ""
if ($allReady) {
    Write-Host "[结论] 运行目录部署完整。若 FileDriven 正在运行，需重启后扩展层装载才生效。" -ForegroundColor Green
} else {
    Write-Host "[结论] 存在缺失，请重新构建对应产品（触发 DeploySelfToRuntime）。" -ForegroundColor Yellow
    exit 1
}
