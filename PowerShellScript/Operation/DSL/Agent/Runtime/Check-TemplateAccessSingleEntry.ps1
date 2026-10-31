#requires -Version 5.1
<#
.SYNOPSIS
    模板访问单入口守卫（TemplateAccessSingleEntry）—— F6 模板访问单入口框架。

.DESCRIPTION
    判据：
      G1 直取静态槽 = 0：除 `ConversationTemplates.cs` 外，任何源文件直取 `HtmlUiHost.Templates` 命中 = 0。
      G2 供源唯一：`ConversationTemplates.Resolve()` 在位（域内统一供源点 = 1）。
      G3 正/负对照：合成样本（直取槽）必被 G1 捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

# 上下文/裁决（根定位 + 违规清单 + 统一出口）
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'TemplateAccessSingleEntry'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  模板访问单入口守卫（TemplateAccessSingleEntry：F6）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$convTemplates = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Rendering\ConversationTemplates.cs'

if (-not (Test-Path -LiteralPath $convTemplates)) {
    $violations.Add("[G2] 缺件（判据不可判）：$convTemplates")
} else {
    $tt = [System.IO.File]::ReadAllText($convTemplates)
    if ($tt -notmatch 'Resolve\s*\(') { $violations.Add('[G2] ConversationTemplates.Resolve 缺失（供源点不在位）') }
}

$direct = [System.Collections.Generic.List[string]]::new()
foreach ($root in @((Join-Path $Root 'Projects\Plug\Agent\src'), (Join-Path $Root 'Frame\CsharpPrj'))) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    foreach ($f in (Get-ChildItem -LiteralPath $root -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -eq 'ConversationTemplates.cs') { continue }
        if ([System.IO.File]::ReadAllText($f.FullName) -match '\bHtmlUiHost\.Templates\b') { $direct.Add($f.Name) }
    }
}
if ($direct.Count -gt 0) {
    $violations.Add("[G1] 直取 HtmlUiHost.Templates 命中 $($direct.Count) 处（应 0；统一经 ConversationTemplates.Resolve）：$($direct -join ', ')")
}

# 正/负对照
if ((Get-Count 'var t = HtmlUiHost.Templates;' '\bHtmlUiHost\.Templates\b') -eq 0) {
    $violations.Add('[G3] 负对照失效：直取槽合成样本未被判据捕获')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'TemplateAccessSingleEntry'
