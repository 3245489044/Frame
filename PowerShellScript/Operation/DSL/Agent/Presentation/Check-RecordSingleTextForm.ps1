#requires -Version 5.1
<#
.SYNOPSIS
    记录单一文本形态守卫（RecordSingleTextForm）—— 详情态只保留整记录编辑器 JSON，无平行载荷文本。

.DESCRIPTION
    判据（B3/RED-06）：
      G1 零平行文本：Agent src 内 `DetailPayloadJson` 引用 = 0（载荷视图改由记录态派生）。
      G2 记录态在位：`DetailRecordJson` 在位。
      G3 正/负对照：合成样本（DetailPayloadJson 引用）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯五级）。
.PARAMETER Check
    校验模式。
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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'RecordSingleTextForm'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  记录单一文本形态守卫（RecordSingleTextForm）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$src = Join-Path $Root 'Projects\Plug\Agent\src'
$refs = 0
$hasRecord = $false
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $c = Get-Count $t 'DetailPayloadJson'
    if ($c -gt 0) { $refs += $c; $violations.Add("[G1] DetailPayloadJson 引用：$($f.Name) × $c") }
    if (Get-Count $t 'DetailRecordJson' -gt 0) { $hasRecord = $true }
}
if (-not $hasRecord) { $violations.Add('[G2] 未见 DetailRecordJson（记录态缺失）') }

# 正/负对照
if ((Get-Count 'state.DetailPayloadJson = x;' 'DetailPayloadJson') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（DetailPayloadJson 引用）未被捕获')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'RecordSingleTextForm'
