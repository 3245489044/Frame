#requires -Version 5.1
<#
.SYNOPSIS
    文本截断单源守卫（TextClipSingleSource）—— 截断策略字面量仅 TextClipPolicy 一处。

.DESCRIPTION
    判据（B1/DUP-02）：
      G1 塑形侧单源：Modules/Node 内截断字面量（`[..max] + "…"`）命中恰 1（TextClipPolicy）。
      G2 消费方委托：DeclaredShaper 经 TextClipPolicy 取值（含 `TextClipPolicy.Summary`），无自造截断。
      G3 存储侧收敛：StorageFramework 内截断实现仅 SqliteTableReader 一处（Markdown 单元格渲染关注点）。
      G4 正/负对照：合成样本（第二处截断）必被捕获。

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
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  文本截断单源守卫（TextClipSingleSource）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$nodeRoot = Join-Path $Root 'Frame\CsharpPrj\Modules\Node'
$sfRoot   = Join-Path $Root 'Frame\CsharpPrj\Plate\DataManage\StorageFramework'

if (Test-Path -LiteralPath $nodeRoot) {
    $hits = 0
    foreach ($f in (Get-ChildItem -Path $nodeRoot -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
        $hits += Get-Count ([System.IO.File]::ReadAllText($f.FullName)) '\[\.\.max\]\s*\+\s*"…"'
    }
    if ($hits -ne 1) { $violations.Add("[G1] Modules/Node 截断字面量命中 $hits 处（应恰 1 = TextClipPolicy）") }

    $shaper = Join-Path $nodeRoot 'Gather\DeclaredShaper.cs'
    if (Test-Path -LiteralPath $shaper) {
        $st = [System.IO.File]::ReadAllText($shaper)
        if ($st -notmatch 'TextClipPolicy\.Summary') { $violations.Add('[G2] DeclaredShaper 未经 TextClipPolicy.Summary 取值') }
        if (Get-Count $st '\[\.\.max\]\s*\+') { $violations.Add('[G2] DeclaredShaper 仍含自造截断') }
    }
} else {
    $violations.Add("[G1] 缺件：$nodeRoot")
}

if (Test-Path -LiteralPath $sfRoot) {
    $sfHits = 0
    foreach ($f in (Get-ChildItem -Path $sfRoot -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
        $sfHits += Get-Count ([System.IO.File]::ReadAllText($f.FullName)) '\[\.\.max\]\s*\+\s*"…"'
    }
    if ($sfHits -gt 1) { $violations.Add("[G3] StorageFramework 截断实现命中 $sfHits 处（应 ≤1 = SqliteTableReader 单元格渲染）") }
}

# 正/负对照
if ((Get-Count 'var s = flat[..max] + "…";' '\[\.\.max\]\s*\+\s*"…"') -eq 0) {
    $violations.Add('[G4] 负对照失效：合成样本（截断字面量）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3 截断单源通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
