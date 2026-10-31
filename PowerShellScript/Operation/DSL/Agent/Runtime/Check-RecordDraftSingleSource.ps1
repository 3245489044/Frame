#requires -Version 5.1
<#
.SYNOPSIS
    记录草稿单源守卫（RecordDraftSingleSource）—— F5 记录草稿单源框架。

.DESCRIPTION
    判据：
      G1 同值双字段清零：旧字段 `DetailSnapshot` 引用 = 0（唯一记录态 = DetailRecordJson）。
      G2 基线语义显式：`DetailBaselineJson` 在位；基线写入点 ≥ 1；`RestoreRecord` 读 `DetailBaselineJson`。
      G3 正/负对照：合成样本（含 DetailSnapshot）必被 G1 捕获。

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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'RecordDraftSingleSource'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  记录草稿单源守卫（RecordDraftSingleSource：F5）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$batchFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs'
$stateFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Interaction\AgentUiDbPageState.cs'

$refs = 0
foreach ($p in @($batchFile, $stateFile)) {
    if (Test-Path -LiteralPath $p) { $refs += (Get-Count ([System.IO.File]::ReadAllText($p)) '\bDetailSnapshot\b') }
}
if ($refs -gt 0) { $violations.Add("[G1] 旧同值字段 DetailSnapshot 引用 = $refs（应 0；唯一记录态 = DetailRecordJson）") }

if (-not (Test-Path -LiteralPath $stateFile)) {
    $violations.Add("[G2] 缺件（判据不可判）：$stateFile")
} elseif ([System.IO.File]::ReadAllText($stateFile) -notmatch 'DetailBaselineJson') {
    $violations.Add('[G2] 基线字段 DetailBaselineJson 缺失（基线语义未显式）')
}

if (Test-Path -LiteralPath $batchFile) {
    $bt = [System.IO.File]::ReadAllText($batchFile)
    if ((Get-Count $bt 'DetailBaselineJson\s*=') -lt 1) { $violations.Add('[G2] 基线写入点 = 0（应 ≥1：装载写一次、保存后更新）') }
    if ($bt -notmatch 'state\.DetailRecordJson\s*=\s*state\.DetailBaselineJson') {
        $violations.Add('[G2] 恢复路径未读 DetailBaselineJson（dbRevert 读点须指向 Baseline）')
    }
    if ($bt -notmatch 'DetailBaselineJson\s*=\s*state\.DetailRecordJson') {
        $violations.Add('[G2] 保存成功后未更新基线（DetailBaselineJson = DetailRecordJson）')
    }
}

# 正/负对照
if ((Get-Count 'state.DetailSnapshot = docText;' '\bDetailSnapshot\b') -eq 0) {
    $violations.Add('[G3] 负对照失效：旧字段合成样本未被判据捕获')
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'RecordDraftSingleSource'
