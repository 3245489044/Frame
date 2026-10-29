#requires -Version 5.1
<#
.SYNOPSIS
    聚拢闭合守卫（GatherClosure）—— 直呼域方法 / 未声明写回的可复算清单（G0 → G2）。
.DESCRIPTION
    判据（结构事实：源码 file:line + 符号名，非执行者叙述）：
      C1 读侧直呼：域取数方法不得绕过 GatherHub（允许集 = producer 实现文件）。
      C2 写侧直呼：DOM / 节点 / 归档写回不得绕过 Hub.Emit（同上）。
      C3 棘轮：违规数 ≤ 基线（Baselines\Process\GatherClosure.psd1，只降不升）。
      C4 负对照：合成直呼样本必须被判据捕获（防判据恒真）。
    模式：默认报告（不设退出码）；-Check 时超基线 → 退出码 1。
.PARAMETER Root
    仓库根（默认由本脚本位置上溯五级）。
.PARAMETER Check
    校验模式（超基线判红）。
.PARAMETER ShowDetail
    输出逐个命中位置。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢闭合守卫（GatherClosure）'
Write-Host '============================================================'

$patterns = @(
    @{ Domain = 'conversation'; Symbol = 'ReadScopeConversationAsync'; Regex = '\bReadScopeConversationAsync\s*\(' },
    @{ Domain = 'histories';    Symbol = 'GetHistoryAsync';            Regex = '\bGetHistoryAsync\s*\(' },
    @{ Domain = 'histories';    Symbol = 'ListHistoriesAsync';         Regex = '\bListHistoriesAsync\s*\(' },
    @{ Domain = 'badges';       Symbol = 'BuildRuntimeBadgesAsync';    Regex = '\bBuildRuntimeBadgesAsync\s*\(' },
    @{ Domain = 'cost';         Symbol = 'CostBadgeProvider.BuildAsync'; Regex = '\bCostBadgeProvider\.BuildAsync\s*\(' },
    @{ Domain = 'prompt';       Symbol = 'PromptSurfaceResolver.ResolveAsync'; Regex = '\bPromptSurfaceResolver\.ResolveAsync\s*\(' },
    @{ Domain = 'board';        Symbol = 'TryReadHistoryBoardAsync';   Regex = '\bTryReadHistoryBoardAsync\s*\(' },
    @{ Domain = 'dom';          Symbol = 'WriteOutputAsync';           Regex = '\bWriteOutputAsync\s*\(' },
    @{ Domain = 'node';         Symbol = 'NodeWriteGate';              Regex = '\bNodeWriteGate\b' }
)

# 允许集（producer 实现侧）：声明了 produce/need 的唯一落点；其余直呼即违规。
$allowFileNames = @(
    'ConversationViewService.Nodes.cs',
    'GatherHub.cs'
)

# 基线（单源；禁内联数值）。
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name GatherClosure
$maxViolations = [int]$baseline.MaxViolations.Value

function Test-GatherClosureText
{
    param([string]$Text, [string]$FileName)

    $hits = [System.Collections.Generic.List[object]]::new()
    if ($allowFileNames -contains $FileName) { return , $hits }

    $lineNo = 0
    foreach ($line in ($Text -split "`n")) {
        $lineNo++
        $trimmed = $line.TrimStart()
        # 跳过注释行（判据载体 = 代码事实，不是文档）。
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('///') -or $trimmed.StartsWith('*')) { continue }
        foreach ($p in $patterns) {
            if ($line -match $p.Regex) {
                $hits.Add([pscustomobject]@{
                    File = $FileName; Line = $lineNo; Domain = $p.Domain; Symbol = $p.Symbol
                })
            }
        }
    }
    return , $hits
}

$srcRoot = Join-Path $Root 'Projects\Plug\Agent\src'
$files = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' })

$hits = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($hit in (Test-GatherClosureText -Text $text -FileName $file.Name)) {
        $hit | Add-Member -NotePropertyName Path -NotePropertyValue $file.FullName -Force
        $hits.Add($hit)
    }
}

# ---------------- 负对照（防假绿） ----------------
$probe = "var x = await svc.GetHistoryAsync(id, ct);"
if (@(Test-GatherClosureText -Text $probe -FileName 'Probe.cs').Count -eq 0) {
    Write-Host '[FAIL] 负对照失效：合成直呼样本未被捕获（判据恒真）' -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

$count = @($hits).Count
if ($ShowDetail) {
    foreach ($hit in $hits) {
        Write-Host ("  {0}:{1}  [{2}] {3}" -f $hit.File, $hit.Line, $hit.Domain, $hit.Symbol) -ForegroundColor Gray
    }
}

Write-Host ("  实测直呼 {0} 处 / 基线上限 {1} 处（只降不升）" -f $count, $maxViolations) -ForegroundColor Gray

if ($count -le $maxViolations) {
    Write-Host '[OK] 聚拢闭合守卫：未超基线（直呼清单棘轮在位）' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] 直呼新增 {0} 处（实测 {1} > 基线 {2}）：须改走 GatherHub" -f ($count - $maxViolations), $count, $maxViolations) -ForegroundColor Yellow
foreach ($hit in $hits) {
    Write-Host ("  - {0}:{1}  [{2}] {3}" -f $hit.File, $hit.Line, $hit.Domain, $hit.Symbol) -ForegroundColor Yellow
}

if ($Check) { exit 1 }
exit 0
