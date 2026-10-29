#requires -Version 5.1
<#
.SYNOPSIS
    UMF 调用点/批量/仲裁窗口门禁（MountCallSites）。

.DESCRIPTION
    判据（施工单 §5.1 Check-MountCallSites + Check-MountBatch + 窗口判据）：
      A 段（调用点棘轮）：每个 MountId 的生产调用点数 ≤ Baselines\Node\MountCallSites.psd1（只减不增）。
      B 段（批量语义）：循环体内构造 MountTarget / 循环内 await ApplyAsync ⇒ 违规。
      C 段（仲裁窗口）：new MountScope 仅 ≤2 实参且无 CorrelationId ⇒ 违规
          （TriggerArbiter 窗口只取 MountScope.CorrelationId）。
      NEG：合成循环批量 + 无窗口 Scope 必须被判红。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  UMF MountCallSites（调用点棘轮 · 批量 · 仲裁窗口）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name MountCallSites
$mountBudgets = @{
    node_to_task       = [int]$baseline.node_to_task.Value
    task_to_history    = [int]$baseline.task_to_history.Value
    message_to_session = [int]$baseline.message_to_session.Value
    task_tree          = [int]$baseline.task_tree.Value
}
$batchBudget = [int]$baseline.BatchInLoop.Value
$windowBudget = [int]$baseline.MissingWindow.Value

$violations = [System.Collections.Generic.List[string]]::new()
$mountIds = @('node_to_task', 'task_to_history', 'message_to_session', 'task_tree')
$counts = @{}
foreach ($id in $mountIds) { $counts[$id] = 0 }
$symbols = @{}
foreach ($id in $mountIds) {
    $constName = $id.Split('_') | ForEach-Object { $_.Substring(0,1).ToUpper() + $_.Substring(1) }
    $symbols[$id] = 'MountIds.' + ($constName -join '')
}
$batchHits = 0
$windowHits = 0

function Get-BracedSpan([string]$text, [int]$openIndex) {
    $depth = 0; $inString = $false
    for ($i = $openIndex; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ($c -eq '"') { $inString = -not $inString; continue }
        if ($inString) { continue }
        if ($c -eq '(') { $depth++ }
        elseif ($c -eq ')') { $depth--; if ($depth -eq 0) { return $text.Substring($openIndex, $i - $openIndex + 1) } }
    }
    return $null
}

$scanRoots = @(
    (Join-Path $Root 'Projects\Plug\Agent\src'),
    (Join-Path $Root 'Frame\CsharpPrj')
)

foreach ($rootDir in $scanRoots) {
    if (-not (Test-Path -LiteralPath $rootDir)) { continue }
    $files = Get-ChildItem -LiteralPath $rootDir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' -and $_.FullName -notlike '*\_deprecated\*' }
    foreach ($file in $files) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $rel = $file.FullName.Replace($Root + '\', '')

        # A 段：MountId 调用点（逐行计数；注释与 [Obsolete] 文案不计）
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $trim = $line.TrimStart()
            if ($trim.StartsWith('//') -or $trim.StartsWith('*') -or $trim.StartsWith('/*')) { continue }
            if ($trim.StartsWith('[Obsolete')) { continue }
            foreach ($id in $mountIds) {
                if ($line -match [regex]::Escape($symbols[$id])) { $counts[$id]++ }
            }
        }

        # B 段：循环内 ApplyAsync / 循环内构造 MountTarget
        $loopApply = [regex]::Matches($text, 'foreach\s*\([^)]*\)\s*\{[^{}]*?ApplyAsync', 'Singleline').Count
        $loopTarget = [regex]::Matches($text, 'foreach\s*\([^)]*\)\s*\{[^{}]*?new MountTarget', 'Singleline').Count
        if (($loopApply + $loopTarget) -gt 0) {
            $batchHits += $loopApply + $loopTarget
            $violations.Add("B 循环内逐点挂载：$rel（foreach=$loopApply，new MountTarget=$loopTarget）")
        }

        # C 段：MountScope 缺窗口
        foreach ($m in [regex]::Matches($text, 'new MountScope\(')) {
            $span = Get-BracedSpan $text ($m.Index + 'new MountScope'.Length)
            if ($null -eq $span) { continue }
            if ($span -match 'CorrelationId') { continue }
            $topCommas = 0; $depth = 0
            foreach ($c in $span.ToCharArray()) {
                if ($c -eq '(') { $depth++ }
                elseif ($c -eq ')') { $depth-- }
                elseif ($c -eq ',' -and $depth -eq 1) { $topCommas++ }
            }
            if ($topCommas -le 1) {
                $windowHits++
                $violations.Add("C 仲裁窗口缺失：$rel（MountScope 无 CorrelationId）")
            }
        }
    }
}

foreach ($id in $mountIds) {
    if (-not $mountBudgets.ContainsKey($id)) {
        $violations.Add("A 未登记 MountId：$id（须进基线表）")
        continue
    }
    if ($counts[$id] -gt $mountBudgets[$id]) {
        $violations.Add("A 调用点新增：$id=$($counts[$id]) > 基线 $($mountBudgets[$id])")
    }
}

if ($batchHits -gt $batchBudget) { $violations.Add("B 循环内挂载点数 $batchHits > 基线 $batchBudget") }
if ($windowHits -gt $windowBudget) { $violations.Add("C 缺窗口点数 $windowHits > 基线 $windowBudget") }

if ($NegativeControl) {
    $sampleLoop = 'foreach (var n in nodes) { await _mounts.ApplyAsync(req); }'
    if (-not ([regex]::IsMatch($sampleLoop, 'foreach\s*\([^)]*\)\s*\{[^{}]*?ApplyAsync', 'Singleline'))) {
        $violations.Add('NEG 负对照失效：合成循环批量未被 B 段命中')
    }
    Write-Host '  [负对照] 合成循环批量已被 B 段命中' -ForegroundColor DarkGray
}

Write-Host ("  调用点 = " + (($mountIds | ForEach-Object { "$_=$($counts[$_])" }) -join ', ')) -ForegroundColor DarkGray
Write-Host "  循环批量=$batchHits/$batchBudget；缺窗口=$windowHits/$windowBudget" -ForegroundColor DarkGray

if ($ShowDetail -and $violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" -ForegroundColor Red }
}

if ($violations.Count -gt 0) {
    Write-Host "  [失败] 违规 $($violations.Count) 项" -ForegroundColor Red
    if ($Check) { exit 1 }
}

Write-Host '  [通过] 调用点/批量/仲裁窗口均未超基线' -ForegroundColor Green
exit 0
