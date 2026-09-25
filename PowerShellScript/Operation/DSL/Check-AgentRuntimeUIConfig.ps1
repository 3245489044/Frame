#requires -Version 5.1
<#
.SYNOPSIS
    JSON 驱动 UI 配置自检器 —— 布局重叠 + 绑定链一致性（编写期回归防护）。

.DESCRIPTION
    校验 JsonConfig/*.json 形式的 UI 配置（views/controls/layout/bindings 契约）：
      检查1 布局重叠  同父兄弟控件（排除 TabControl/TabPage 固有堆叠）归一化包围盒相交 → Warning
      检查2 绑定悬空  bindings.source / bindings.output 指向不存在的控件名           → Error
      检查3 函数未注册 events.click 函数名不在已知注册函数清单                      → Warning

    用途：把「手写归一化坐标漂移 → 静默重叠」「绑定源/输出改名 → 静默断链」这两类
    AgentRuntimeUI.json 历史缺陷，转化为编写期可观测报告，防止回归。
    对齐既有 PowerShellScript/DSL/Check-ClinerulesReferences.ps1 模式（独立脚本、无运行时副作用）。

.PARAMETER ConfigPath
    待校验的 UI 配置 JSON（默认 Projects/Information/DrivenConfig/JsonConfig/AgentRuntimeUI.json ——
    FileDriven 的 DrivenConfig 资源已整体迁入 Information 资源仓，见 Modules\FileDriven\FileDrivenTestUtils.ps1）。

.PARAMETER WorkFlowDir
    已知注册函数（工作流名）来源目录（默认 Projects/Information/DrivenConfig/WorkFlow），
    其下 *.json 的 workflowName 字段视为已注册函数。

.PARAMETER FailOnWarning
    指定后 Warning 也导致非零退出码（用于 CI 严格模式）。

.EXAMPLE
    ./Check-AgentRuntimeUIConfig.ps1
    ./Check-AgentRuntimeUIConfig.ps1 -ConfigPath .\MathFunctions.json -FailOnWarning
#>
[CmdletBinding()]
param(
    # FileDriven 的 DrivenConfig 资源已整体迁入 Information 资源仓（见 Modules\FileDriven\FileDrivenTestUtils.ps1）；
    # 上溯四级 = 仓库根（本脚本位于 Frame\PowerShellScript\Operation\DSL\）。
    [string]$ConfigPath = (Join-Path $PSScriptRoot '..\..\..\..\Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'),
    [string]$WorkFlowDir = (Join-Path $PSScriptRoot '..\..\..\..\Projects\Information\DrivenConfig\WorkFlow'),
    [switch]$FailOnWarning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExitCode = 0
$Errors = [System.Collections.Generic.List[string]]::new()
$Warnings = [System.Collections.Generic.List[string]]::new()

function Add-Error([string]$m) { $script:Errors.Add($m); $script:ExitCode = 1 }
function Add-Warning([string]$m) { $script:Warnings.Add($m) }

# StrictMode 安全取属性（对象为 null 或属性不存在 → 返回 $null，不抛错）
function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}


# ---- 检查0：JSON 可解析 ----
if (-not (Test-Path -LiteralPath $ConfigPath)) {
    Write-Error "配置不存在: $ConfigPath"
    exit 2
}
try {
    $config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json -Depth 64
}
catch {
    Write-Error "配置 JSON 解析失败: $($_.Exception.Message)"
    exit 2
}

# ---- 收集全部控件名（递归） ----
# 作用域 = **组合后 UI**，不只本文件：绑定的 output/source 按名解析发生在 MainLayout 组合后的控件树上，
# 跨文件片段绑定是合法契约（实证：MainLayout.json 的 outputTextBox 显式注释「绑定 output=outputTextBox 保持兼容」）。
# 故判据面 = 本文件 views/templates + **同目录全部 JsonConfig 片段**，否则跨文件绑定被误报为「悬空」。
$controlNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Collect-Names([array]$nodes) {
    foreach ($c in $nodes) {
        $name = Get-Prop $c 'name'
        if ($name) { [void]$controlNames.Add([string]$name) }
        $children = Get-Prop $c 'controls'
        if ($children) { Collect-Names $children }
    }
}
function Collect-ConfigNames($cfg) {
    if ($null -eq $cfg) { return }
    foreach ($view in @(Get-Prop $cfg 'views')) {
        $vc = if ($null -ne $view) { Get-Prop $view 'controls' } else { $null }
        if ($vc) { Collect-Names $vc }
    }
    $tpl = Get-Prop $cfg 'templates'
    if ($tpl -and $tpl.PSObject) {
        foreach ($t in $tpl.PSObject.Properties) {
            $tn = Get-Prop $t.Value 'controls'
            if ($tn) { Collect-Names $tn }
        }
    }
}
Collect-ConfigNames $config
$configDir = Split-Path -Parent $ConfigPath
$configLeaf = Split-Path -Leaf $ConfigPath
foreach ($sib in @(Get-ChildItem -LiteralPath $configDir -Filter *.json -File -ErrorAction SilentlyContinue)) {
    if ($sib.Name -eq $configLeaf) { continue }
    try { Collect-ConfigNames (Get-Content -Raw -LiteralPath $sib.FullName | ConvertFrom-Json -Depth 64) }
    catch { Add-Warning "片段解析失败（未纳入控件名集合）: $($sib.Name) — $($_.Exception.Message)" }
}

# ---- 检查1：布局重叠（兄弟级，排除 TabControl/TabPage 固有堆叠） ----
$overlapEpsilon = 1e-4   # 亚像素容差，避免浮点相邻误报
function Test-Overlap($a, $b) {
    $aL = [double]$a.layout.left;  $aT = [double]$a.layout.top
    $aW = [double]$a.layout.width; $aH = [double]$a.layout.height
    $bL = [double]$b.layout.left;  $bT = [double]$b.layout.top
    $bW = [double]$b.layout.width; $bH = [double]$b.layout.height
    $aR = $aL + $aW; $aB = $aT + $aH
    $bR = $bL + $bW; $bB = $bT + $bH
    $ovx = ($aR - $bL) -gt $overlapEpsilon -and ($bR - $aL) -gt $overlapEpsilon
    $ovy = ($aB - $bT) -gt $overlapEpsilon -and ($bB - $aT) -gt $overlapEpsilon
    return ($ovx -and $ovy)
}

function Check-Container([array]$nodes, [string]$ctx, [bool]$skipOverlap) {
    if (-not $skipOverlap -and $nodes.Count -ge 2) {
        for ($i = 0; $i -lt $nodes.Count; $i++) {
            for ($k = $i + 1; $k -lt $nodes.Count; $k++) {
                $a = $nodes[$i]; $b = $nodes[$k]
                $al = Get-Prop $a 'layout'; $bl = Get-Prop $b 'layout'
                if ($al -and $bl -and (Test-Overlap $a $b)) {
                    Add-Warning "布局重叠 [$ctx]: $(Get-Prop $a 'name') x $(Get-Prop $b 'name')"
                }
            }
        }
    }
    foreach ($n in $nodes) {
        $children = Get-Prop $n 'controls'
        if ($children) {
            # TabControl 的直系子节点是 TabPage（固有堆叠），跳过其相互重叠；其内部仍正常递归
            $childSkip = ((Get-Prop $n 'type') -eq 'tabcontrol')
            Check-Container $children (Get-Prop $n 'name') $childSkip
        }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Check-Container $vc $view.name $false }
}


# ---- 检查2：绑定源 / 输出控件名悬空 ----
function Check-Bindings($nodes) {
    foreach ($c in $nodes) {
        $events = Get-Prop $c 'events'
        if ($events) {
            $bind = Get-Prop $events 'bindings'
            if ($bind) {
                $output = Get-Prop $bind 'output'
                if ($output -and -not $controlNames.Contains([string]$output)) {
                    Add-Error "绑定输出悬空: $(Get-Prop $c 'name') → output=$output"
                }
                $parameters = Get-Prop $bind 'parameters'
                if ($parameters) {
                    foreach ($p in $parameters) {
                        $sourceType = Get-Prop $p 'sourceType'
                        $source = Get-Prop $p 'source'
                        if ($sourceType -eq 'control' -and $source -and -not $controlNames.Contains([string]$source)) {
                            Add-Error "绑定参数源悬空: $(Get-Prop $c 'name') → source=$source"
                        }
                    }
                }
            }
        }
        $children = Get-Prop $c 'controls'
        if ($children) { Check-Bindings $children }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Check-Bindings $vc }
}

# ---- 检查3：click / initCommands 函数名注册（前缀库 + 工作流名清单） ----
$knownFunctions = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
function Test-KnownPrefix([string]$name) {
    return $name -like 'Ui_*' -or $name -like 'AgentUi_*'
}
# 工作流函数名（WorkFlowDir 下各 *.json 的 workflowName）视为已注册
if (Test-Path -LiteralPath $WorkFlowDir) {
    Get-ChildItem -LiteralPath $WorkFlowDir -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $wf = Get-Content -Raw -LiteralPath $_.FullName | ConvertFrom-Json -Depth 32
            if ($wf.workflowName) { [void]$knownFunctions.Add([string]$wf.workflowName) }
        }
        catch { }
    }
}
function Check-ClickFunctions($nodes) {
    foreach ($c in $nodes) {
        $events = Get-Prop $c 'events'
        if ($events) {
            $click = Get-Prop $events 'click'
            if ($click) {
                foreach ($fn in @($click)) {
                    if ($knownFunctions.Contains([string]$fn) -or (Test-KnownPrefix ([string]$fn))) {
                        continue
                    }
                    Add-Warning "click 函数未在已知注册清单: $(Get-Prop $c 'name') → $fn（若为合法注册函数请加入 allowlist）"
                }
            }
        }
        $children = Get-Prop $c 'controls'
        if ($children) { Check-ClickFunctions $children }
    }
}
foreach ($view in $config.views) {
    $vc = Get-Prop $view 'controls'
    if ($vc) { Check-ClickFunctions $vc }
}

if ($config.initCommands) {
    foreach ($cmd in $config.initCommands) {
        if ($cmd -match 'exec\s+([A-Za-z0-9_]+)') {
            $fn = $Matches[1]
            if (-not $knownFunctions.Contains($fn) -and -not (Test-KnownPrefix $fn)) {
                Add-Warning "initCommands 函数未在已知注册清单: $fn"
            }
        }
    }
}

# ---- 报告 ----
Write-Host "=== Check-AgentRuntimeUIConfig ===" -ForegroundColor Cyan
Write-Host ("配置: {0}" -f $ConfigPath)
Write-Host ("控件总数: {0}" -f $controlNames.Count)
Write-Host ("已知注册函数(工作流): {0}" -f $knownFunctions.Count)
Write-Host "--- 布局重叠 / 函数未注册 (Warning) ---" -ForegroundColor Yellow
if ($Warnings.Count -eq 0) { Write-Host "  (无)" }
else { $Warnings | ForEach-Object { Write-Host ("  [!] " + $_) -ForegroundColor Yellow } }
Write-Host "--- 绑定悬空 (Error) ---" -ForegroundColor Red
if ($Errors.Count -eq 0) { Write-Host "  (无)" }
else { $Errors | ForEach-Object { Write-Host ("  [X] " + $_) -ForegroundColor Red } }

if ($Errors.Count -eq 0 -and ($Warnings.Count -eq 0 -or -not $FailOnWarning)) {
    Write-Host "结果: 通过" -ForegroundColor Green
}
else {
    Write-Host "结果: 存在问题" -ForegroundColor Red
}

exit $ExitCode
