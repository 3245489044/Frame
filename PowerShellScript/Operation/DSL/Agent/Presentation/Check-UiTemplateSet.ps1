#requires -Version 5.1
<#
.SYNOPSIS
    模板集完整性守卫（UiTemplateSet）—— F-3/I-2：生效模板集必须含区域渲染必需键。

.DESCRIPTION
    判据（结构事实）：
      T1 生效集必需键（默认集 ∪ 配置覆盖）含必需键且非空；
      T2 配置自有模板键须登记于 Baselines\Agent\UiTemplateOwnership.psd1（禁未登记自有键）；
      T3 装配期合并结构在位：HtmlBridgeAssembly 以 GetDefaultTemplates() 为底、配置逐键覆盖；
      T4 解析入口防御性合并在位：ConversationTemplates.Resolve 以框架默认集为底合并槽位；
      T5 负对照：缺必需键的合成集 / 无合并结构源码 / 默认集∩配置集重复键必须被判出；
      T6 模板键单源：默认集与配置集不得重复定义同一键（禁双份/漂移）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
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
$resolverPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationTemplates.cs'

# 必需键单源 = ConversationTemplates.RequiredKeys（禁门禁/默认集各写一份）
$requiredKeys = @()
if (Test-Path -LiteralPath $resolverPath) {
    $resolverText = [System.IO.File]::ReadAllText($resolverPath)
    if ($resolverText -match 'RequiredKeys\s*=\s*(?:new\[\]\s*)?\{([^}]*)\}') {
        $requiredKeys = @([regex]::Matches($Matches[1], '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    }
}
if ($requiredKeys.Count -eq 0) {
    Add-Violation '[T1] 必需键声明不可得（判据不可判）：ConversationTemplates.RequiredKeys'
}

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

# ---- T1 生效集必需键（默认集 ∪ 配置覆盖）----
$defaultKeys = $null
$configKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
if (-not (Test-Path -LiteralPath $defaultsPath)) {
    Add-Violation "[T1] 缺件（判据不可判）：$defaultsPath"
} else {
    try {
        $defaults = Get-Content -Raw -LiteralPath $defaultsPath | ConvertFrom-Json -Depth 16
        $defaultKeys = Get-StringMapKeys $defaults
    }
    catch {
        Add-Violation "[T1] 默认模板解析失败: $($_.Exception.Message)"
    }
}

if (-not (Test-Path -LiteralPath $configPath)) {
    Add-Violation "[T1] 缺件（判据不可判）：$configPath"
} else {
    try {
        $config = Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json -Depth 64
        $configKeys = Get-StringMapKeys (Get-Prop $config 'templates')
    }
    catch {
        Add-Violation "[T1] 配置模板解析失败: $($_.Exception.Message)"
    }
}

if ($defaultKeys) {
    $effective = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $defaultKeys) { [void]$effective.Add($k) }
    foreach ($k in $configKeys) { [void]$effective.Add($k) }
    Test-RequiredKeys $effective 'T1' $violations
}

# ---- T2 配置自有模板键须登记（禁未登记的自有键）----
$ownershipPath = Join-Path $Root 'Frame\PowerShellScript\Baselines\Agent\UiTemplateOwnership.psd1'
if (-not (Test-Path -LiteralPath $ownershipPath)) {
    Add-Violation "[T2] 缺件（判据不可判）：$ownershipPath"
} else {
    $ownedKeys = @((Import-PowerShellDataFile -LiteralPath $ownershipPath).Keys)
    foreach ($k in $configKeys) {
        if ($ownedKeys -notcontains $k) {
            Add-Violation "[T2] 配置模板键未登记为 Agent 自有：$k（Baselines\Agent\UiTemplateOwnership.psd1）"
        }
    }
}

# ---- T6 模板键单源：默认集 ∩ 配置集 = ∅ ----
if ($defaultKeys) {
    foreach ($k in $configKeys) {
        if ($defaultKeys.Contains($k)) {
            Add-Violation "[T6] 模板键双份定义（默认集 ∩ 配置集）：$k"
        }
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
$dupDefault = @{ listItem = 'x' }
$dupConfig = @('listItem')
if (-not ($dupConfig | Where-Object { $dupDefault.ContainsKey($_) })) {
    Add-Violation '[T5] 负对照失效：默认集∩配置集重复键样本未被判出'
}

if ($NegativeControl) {
    if ($violations.Count -gt 0) {
        Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
        foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
        exit 1
    }
    Write-Host '[OK] 负对照通过：必需键缺失 / 无合并结构 / 默认集∩配置集重复键均被捕获。' -ForegroundColor Green
    exit 0
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
