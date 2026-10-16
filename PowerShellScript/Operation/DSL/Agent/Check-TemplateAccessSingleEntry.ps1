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
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  模板访问单入口守卫（TemplateAccessSingleEntry：F6）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$convTemplates = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationTemplates.cs'

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

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 模板访问单入口通过（直取槽 = 0 + 供源唯一）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
