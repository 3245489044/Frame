#requires -Version 5.1
<#
.SYNOPSIS
    模板集完整性守卫（UiTemplateSet）—— F-3/I-2：生效模板集必须含区域渲染必需键。

.DESCRIPTION
    判据（结构事实）：
      T1 框架默认模板含必需键（layoutShell/segGroup/emptyDiv/composer/segItem/listItem/foldItem/emptyP）且非空；
      T2 配置模板覆盖键 ⊆ 框架默认键集（同一交互契约禁两份声明漂移）；
      T3 装配期合并结构在位：HtmlBridgeAssembly 以 GetDefaultTemplates() 为底、配置逐键覆盖；
      T4 解析入口防御性合并在位：ConversationTemplates.Resolve 以框架默认集为底合并槽位；
      T5 负对照：缺 segGroup 的合成默认集、无合并结构的合成源码必须被判缺（防判据恒真）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$m) { $script:violations.Add($m) }

Write-Host ''
Write-Host '============================================================'
Write-Host '  模板集完整性守卫（UiTemplateSet：F-3/I-2）'
Write-Host '============================================================'

$defaultsPath = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'
$configPath = Join-Path $Root 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'
$assemblyPath = Join-Path $Root 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlHosting\HtmlBridgeAssembly.cs'
$resolverPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationTemplates.cs'

$requiredKeys = @('layoutShell', 'segGroup', 'emptyDiv', 'composer', 'segItem', 'listItem', 'foldItem', 'emptyP')

function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Get-StringMapKeys($obj) {
    $set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    if ($null -eq $obj) { return $set }
    foreach ($p in $obj.PSObject.Properties) {
        if ($p.Value -is [string]) { [void]$set.Add($p.Name) }
    }
    return $set
}

function Test-RequiredKeys($map, [string]$source, [System.Collections.Generic.List[string]]$out) {
    foreach ($k in $requiredKeys) {
        if (-not $map.Contains($k)) { $out.Add("[$source] 缺必需模板键: $k") }
    }
}

# ---- T1 默认模板必需键 ----
$defaultKeys = $null
if (-not (Test-Path -LiteralPath $defaultsPath)) {
    Add-Violation "[T1] 缺件（判据不可判）：$defaultsPath"
} else {
    try {
        $defaults = Get-Content -Raw -LiteralPath $defaultsPath | ConvertFrom-Json -Depth 16
        $defaultKeys = Get-StringMapKeys $defaults
        Test-RequiredKeys $defaultKeys 'T1' $violations
    }
    catch {
        Add-Violation "[T1] 默认模板解析失败: $($_.Exception.Message)"
    }
}

# ---- T2 配置覆盖键 ⊆ 默认键集 ----
if ($defaultKeys -and (Test-Path -LiteralPath $configPath)) {
    try {
        $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json -Depth 64
        $configKeys = Get-StringMapKeys (Get-Prop $config 'templates')
        foreach ($k in $configKeys) {
            if (-not $defaultKeys.Contains($k)) {
                Add-Violation "[T2] 配置覆盖键不存在于框架默认键集: $k"
            }
        }
    }
    catch {
        Add-Violation "[T2] 配置模板解析失败: $($_.Exception.Message)"
    }
}

function Get-Prop($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

# ---- T3 装配期合并结构 ----
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    Add-Violation "[T3] 缺件（判据不可判）：$assemblyPath"
} else {
    $t = [System.IO.File]::ReadAllText($assemblyPath)
    foreach ($needle in @('GetDefaultTemplates()', 'new Dictionary<string, string>(defaults')) {
        if ($t -notmatch [regex]::Escape($needle)) {
            Add-Violation "[T3] 装配期『默认 ⊕ 覆盖』合并结构缺失: $needle"
        }
    }
}

# ---- T4 解析入口防御性合并 ----
if (-not (Test-Path -LiteralPath $resolverPath)) {
    Add-Violation "[T4] 缺件（判据不可判）：$resolverPath"
} else {
    $t = [System.IO.File]::ReadAllText($resolverPath)
    foreach ($needle in @('HtmlAssetRegistry.DefaultTemplates', 'RequiredKeys')) {
        if ($t -notmatch [regex]::Escape($needle)) {
            Add-Violation "[T4] 模板解析入口未以默认集为底合并/未声明必需键: $needle"
        }
    }
}

# ---- T5 负对照（防判据恒真） ----
$negDefault = [pscustomobject]@{ layoutShell = '<div></div>'; emptyDiv = '<div></div>' }
$negOut = [System.Collections.Generic.List[string]]::new()
Test-RequiredKeys (Get-StringMapKeys $negDefault) 'NEG' $negOut
if ($negOut.Count -eq 0) {
    Add-Violation '[T5] 负对照失效：缺 segGroup/composer 的样本未被判缺'
}
$negAssembly = 'var resolved = templates; host.AttachTemplates(resolved);'
if ($negAssembly -match [regex]::Escape('GetDefaultTemplates()')) {
    Add-Violation '[T5] 负对照失效：无合并结构的样本未被判缺'
}

if ($violations.Count -eq 0) {
    Write-Host ("  [成功] T1-T4 通过（必需键齐备 + 覆盖键闭合 + 合并结构在位；默认键数={0}）" -f $defaultKeys.Count) -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
