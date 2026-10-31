#requires -Version 5.1
<#
.SYNOPSIS
    写面保真守卫（UiWriteFidelity）—— F-1/F-8：整页原始 HTML 与 Tree/Bulk 往返等价的结构判据。

.DESCRIPTION
    判据（结构事实：符号/分支在位，禁执行者叙述）：
      G1 Bulk 序列化支持片段根：UiNodeHtml.Append 的 IsText 分支透传 Children（曾整段丢弃 ⇒ 空载荷清空区域）；
      G2 解析侧实体解码：HtmlNodeParser 属性/文本均经 DecodeEntities 落值（否则 data-payload 二次转义）；
      G3 页面侧属性同步在位：reconcile.js buildNode 读取 n.attributes、syncAttrs 做增删同步；
      G4 往返用例在位：UiWriteFidelityTests 语料含 data-name/data-payload/details/open，且断言身份键集合相等；
      G5 负对照：合成「片段根无透传」/「无实体解码」样本必须被判缺（防判据恒真）。

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

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$m) { $script:violations.Add($m) }

Write-Host ''
Write-Host '============================================================'
Write-Host '  写面保真守卫（UiWriteFidelity：F-1/F-8）'
Write-Host '============================================================'

$uiNodeHtml = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\UiNodeHtml.cs'
$parser = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\HtmlNodeParser.cs'
$reconcile = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\scripts\reconcile.js'
$tests = Join-Path $Root 'Environment\CentralTest\CSharpTest\Plate.Tests\HtmlViewer\UiWriteFidelityTests.cs'

function Test-FragmentPassthrough([string]$text) {
    return ($text -match [regex]::Escape('node.Children is { Count: > 0 }')) -and
           ($text -match [regex]::Escape('foreach (var child in node.Children) Append(child, sb);'))
}

function Test-EntityDecode([string]$text) {
    return ($text -match [regex]::Escape('private static string DecodeEntities')) -and
           ($text -match [regex]::Escape('attrs[attrName] = DecodeEntities(value);')) -and
           ($text -match [regex]::Escape('var s = DecodeEntities(text.ToString());'))
}

# ---- G1 片段根序列化 ----
if (-not (Test-Path -LiteralPath $uiNodeHtml)) {
    Add-Violation "[G1] 缺件（判据不可判）：$uiNodeHtml"
} else {
    $t = [System.IO.File]::ReadAllText($uiNodeHtml)
    if (-not (Test-FragmentPassthrough $t)) {
        Add-Violation '[G1] 片段根未透传子节点（Bulk 载荷会为空串 ⇒ innerHTML 清空区域）'
    }
}

# ---- G2 解析侧实体解码 ----
if (-not (Test-Path -LiteralPath $parser)) {
    Add-Violation "[G2] 缺件（判据不可判）：$parser"
} else {
    $t = [System.IO.File]::ReadAllText($parser)
    if (-not (Test-EntityDecode $t)) {
        Add-Violation '[G2] 属性/文本未做 HTML 实体解码（data-payload 会被二次转义为字面实体）'
    }
}

# ---- G3 页面侧属性同步 ----
if (-not (Test-Path -LiteralPath $reconcile)) {
    Add-Violation "[G3] 缺件（判据不可判）：$reconcile"
} else {
    $t = [System.IO.File]::ReadAllText($reconcile)
    foreach ($needle in @('function buildNode', 'n.attributes', 'function syncAttrs')) {
        if ($t -notmatch [regex]::Escape($needle)) {
            Add-Violation "[G3] 键控对齐缺符号：$needle"
        }
    }
}

# ---- G4 往返用例 ----
if (-not (Test-Path -LiteralPath $tests)) {
    Add-Violation "[G4] 缺件：$tests（往返保真用例未登记）"
} else {
    $t = [System.IO.File]::ReadAllText($tests)
    foreach ($needle in @('data-name', 'data-payload', 'details', 'open', 'CollectionAssert.AreEquivalent', 'IsNullOrWhiteSpace(html)')) {
        if ($t -notmatch [regex]::Escape($needle)) {
            Add-Violation "[G4] 往返用例缺判据载体：$needle"
        }
    }
}

# ---- G5 负对照（防判据恒真） ----
$negFragment = 'private static void Append(UiNode node, StringBuilder sb) { if (node.IsText) { sb.Append(EscapeText(node.Text)); return; } }'
if (Test-FragmentPassthrough $negFragment) {
    Add-Violation '[G5] 负对照失效：无透传的片段根样本未被判缺'
}
$negParse = 'if (attrName.Length > 0) attrs[attrName] = value;'
if (Test-EntityDecode $negParse) {
    Add-Violation '[G5] 负对照失效：无实体解码的属性样本未被判缺'
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3/G4 通过（片段根透传 + 实体解码 + 属性同步 + 往返用例）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
