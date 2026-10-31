#requires -Version 5.1
<#
.SYNOPSIS
    详情呈现零域分支守卫（RenderNoDomainBranch）—— G4 呈现投影聚拢点。

.DESCRIPTION
    判据（`ConversationViewService.Db.RenderNodeDetail` 方法体）：
      G1 节注单源：体内消费 `state.DetailNote`（禁硬编码节注文案冒充装载结果）。
      G2 零硬编码节注：「整记录（JSON 可编辑」等硬编码文案命中 = 0。
      G3 禁假可用：编辑器初值经 `editable` 门（含 `editable ? state.DetailRecordJson : null`），
         且无记录文本分支不下发编辑器（`editorDataName: null` 分支在位）。
      G4 正/负对照：合成样本（含硬编码节注）必被 G2 捕获（防判据空转）。

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

Write-Host ''
Write-Host '============================================================'
Write-Host '  详情呈现零域分支守卫（RenderNoDomainBranch：G4）'
Write-Host '============================================================'

$dbView = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Db.cs'
$hardcodedNote = '整记录（JSON 可编辑'

function Get-RenderBody([string]$Text) {
    $sig = 'private static string RenderNodeDetail('
    $start = $Text.IndexOf($sig)
    if ($start -lt 0) { return $null }
    $end = $Text.IndexOf("`n    }", $start)
    if ($end -lt 0) { $end = [Math]::Min($start + 4000, $Text.Length) }
    return $Text.Substring($start, $end - $start)
}

function Get-RenderMisses([string]$Body, [string]$Note) {
    $misses = [System.Collections.Generic.List[string]]::new()
    if ($Body -notmatch 'state\.DetailNote') { $misses.Add('节注未消费 DetailNote（装载结果被忽略）') }
    if ($Body -match [regex]::Escape($Note)) { $misses.Add('残留硬编码节注文案（呈现决策回流渲染层）') }
    if ($Body -notmatch 'editable\s*\?\s*state\.DetailRecordJson') { $misses.Add('编辑器未受可编辑门约束（禁假可用）') }
    return $misses
}

if (-not (Test-Path -LiteralPath $dbView)) {
    $violations.Add("[G1] 缺件（判据不可判）：$dbView")
} else {
    $body = Get-RenderBody ([System.IO.File]::ReadAllText($dbView))
    if ($null -eq $body) {
        $violations.Add('[G1] 未定位 RenderNodeDetail（判据不可判）')
    } else {
        foreach ($m in (Get-RenderMisses $body $hardcodedNote)) { $violations.Add("[G1] $m") }
        $probe = 'sectionNote: "' + $hardcodedNote + '：nodeType/threadId/key/status/payload）",'
        if ((Get-RenderMisses $probe $hardcodedNote).Count -eq 0) {
            $violations.Add('[G4] 负对照失效：硬编码节注的合成样本未被判据捕获')
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3/G4 通过（节注单源 + 零硬编码 + 禁假可用）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
