#requires -Version 5.1
<#
.SYNOPSIS
    操作行承载执行体守卫（OperationRowCarriesActor）—— G5 操作流承载模型。

.DESCRIPTION
    判据：
      G1 行模型含执行体键：`HtmlStreamRenderer` 操作行模型（ToOperationRow）含 `["subagent"]`。
      G2 模板键在位：框架 `actionPanel` 模板行含 `{subagent}`（行模型键 ⇄ 模板键同名）。
      G3 承载单源：`RenderOperations` 体内 `Role.ToString()` 命中 = 0（不禁由 Role 二次猜测）。
        行模型构造点 = 1（`ToOperationRow`，模板路径与内联兜底共享同一模型）。
      G4 正/负对照：合成样本（含 Role 兜底）必被 G3 捕获（防判据空转）。

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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'OperationRowCarriesActor'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  操作行承载执行体守卫（OperationRowCarriesActor：G5）'
Write-Host '============================================================'

$renderer = Join-Path $Root 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge\HtmlStreamRenderer.cs'
$templates = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Assets\templates.default.json'

function Get-RenderOpsBody([string]$Text) {
    $start = $Text.IndexOf('private static string RenderOperations(')
    if ($start -lt 0) { return $null }
    $end = $Text.IndexOf('private static IReadOnlyDictionary<string, object?> ToOperationRow(', $start)
    if ($end -lt 0) { $end = [Math]::Min($start + 4000, $Text.Length) }
    return $Text.Substring($start, $end - $start)
}

function Get-ActorMisses([string]$Body) {
    $misses = [System.Collections.Generic.List[string]]::new()
    if ($Body -match 'Role\.ToString\(\)') { $misses.Add('操作行承载由 Role 二次猜测（禁 Role 兜底）') }
    if ($Body -notmatch 'ToOperationRow') { $misses.Add('操作行未走单一行模型（ToOperationRow）') }
    return $misses
}

if (-not (Test-Path -LiteralPath $renderer)) {
    $violations.Add("[G1] 缺件（判据不可判）：$renderer")
} else {
    $text = [System.IO.File]::ReadAllText($renderer)
    $modelDefs = ([regex]::Matches($text, 'private\s+static\s+IReadOnlyDictionary<string,\s*object\?>\s+ToOperationRow\s*\(')).Count
    if ($modelDefs -ne 1) { $violations.Add("[G3] 操作行模型构造点 = $modelDefs（应恰 1 处 ToOperationRow）") }
    if ($text -notmatch '\["subagent"\]') { $violations.Add('[G1] 操作行模型缺 subagent 键（子智能体不可见）') }
    if ($text -notmatch '\["targets"\]') { $violations.Add('[G1b] 操作行模型缺 targets 键（分发目标集不可见）') }
    $body = Get-RenderOpsBody $text
    if ($null -eq $body) {
        $violations.Add('[G1] 未定位 RenderOperations（判据不可判）')
    } else {
        foreach ($m in (Get-ActorMisses $body)) { $violations.Add("[G3] $m") }
        $probe = '["title"] = string.IsNullOrWhiteSpace(m.Stage) ? m.Role.ToString() : m.Stage,'
        if ((Get-ActorMisses $probe).Count -eq 0) {
            $violations.Add('[G4] 负对照失效：Role 兜底的合成样本未被判据捕获')
        }
    }
}

if (-not (Test-Path -LiteralPath $templates)) {
    $violations.Add("[G2] 缺件（判据不可判）：$templates")
} else {
    $tplText = [System.IO.File]::ReadAllText($templates)
    $line = ($tplText -split "`n" | Where-Object { $_ -match '"actionPanel"\s*:' } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($line)) {
        $violations.Add('[G2] 框架默认模板缺 actionPanel（操作流承载面缺失）')
    } elseif ($line -notmatch '\{subagent\}') {
        $violations.Add('[G2] actionPanel 模板缺 {subagent} 键（行模型键 ⇄ 模板键不同名）')
    }
    if ($line -notmatch '\{\?showTargets\}') {
        $violations.Add('[G2b] actionPanel 模板缺 {?showTargets} 判空守卫（空 targets 会产空 span）')
    }
    if ($line -notmatch '\{targets\}') {
        $violations.Add('[G2b] actionPanel 模板缺 {targets} 键（行模型键 ⇄ 模板键不同名）')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'OperationRowCarriesActor'
