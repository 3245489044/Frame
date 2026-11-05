#requires -Version 5.1
<#
.SYNOPSIS
    域写能力单谓词守卫（WriteCapabilitySinglePredicate）—— 能力判定入口唯一。

.DESCRIPTION
    判据（B3/SCT-01）：
      G1 单谓词在位：`AgentWriteSources.Capability(` 定义 = 1（枚举 WriteCapability 取值域在位）。
      G2 旧三处判定归零：Agent src 内 `IsInlineEditable`/`IsKnownWriteDomain`/`IsRowWritable` 引用 = 0。
      G3 正/负对照：合成样本（旧判定名）必被捕获。

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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'WriteCapabilitySinglePredicate'
$Root = $ctx.Root
$violations = $ctx.Violations
Write-Host ''
Write-Host '============================================================'
Write-Host '  域写能力单谓词守卫（WriteCapabilitySinglePredicate）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$src = Join-Path $Root 'Projects\Plug\Agent\src'
$sources = Join-Path $src 'Agent.Ui\Core\Functions\Batch\AgentWriteSources.cs'
$capDefs = 0
$legacyRefs = 0
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch (Get-GateSourceExcludePattern) })) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $c1 = Get-Count $t 'internal\s+static\s+WriteCapability\s+Capability\s*\('
    if ($c1 -gt 0) { $capDefs += $c1 }
    $c2 = Get-Count $t 'IsInlineEditable|IsKnownWriteDomain|IsRowWritable'
    if ($c2 -gt 0) { $legacyRefs += $c2; $violations.Add("[G2] 旧能力判定引用：$($f.Name) × $c2") }
}
if ($capDefs -ne 1) { $violations.Add("[G1] 单谓词 Capability 定义数 ≠ 1（实际 $capDefs）") }
if (-not (Test-Path -LiteralPath $sources)) { $violations.Add('[G1] 未见 AgentWriteSources.cs') }
elseif ((Get-Count ([System.IO.File]::ReadAllText($sources)) 'enum\s+WriteCapability') -eq 0) {
    $violations.Add('[G1] WriteCapability 枚举缺失')
}

# 正/负对照
if ((Get-Count 'IsInlineEditable(x)' 'IsInlineEditable|IsKnownWriteDomain|IsRowWritable') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（旧判定名）未被捕获')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'WriteCapabilitySinglePredicate'
