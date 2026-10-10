#requires -Version 5.1
<#
.SYNOPSIS
    usharp_get 集中式转换层守卫（V4 语义 / V5 下推 / V6 CAS）。

.DESCRIPTION
    判据（-Check 时违规 → 退出码 1）：
      G1 注册单点（FAIL）：全仓 '.EnsureRegistered()' 调用点 = 1（唯一注册点 = UsharpDataPorts.EnsureSqlFunctions）
      G2 下推表达式同源（FAIL）：SqliteTaskPoolStore / SqliteSnapshotStore 内联 JSON 路径字面量（'$.x'）计数 = 0（须经 UsharpRecordPaths）
      G3 表达式索引落点（FAIL）：'.EnsureUsharpIndex(' 调用数 = 基线，且每处路径参数来自 UsharpRecordPaths
      G4 CAS 单语句（FAIL）：SqliteTaskPoolStore 存在单语句 UPDATE SET [Doc]=@d ... usharp_get(...Status...)=@from
      G5 正/负对照（FAIL）：扫描面非空 + 判据正则能捕获合成样本（防假绿）

    基线：Frame\PowerShellScript\Baselines\UsharpGetParity.psd1（单源，禁内联）
    模式：默认报告模式；-Check 校验模式（违规 → 退出码 1）。

.PARAMETER Root
    仓库根（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UsharpGetParity

$storesDir  = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Sqlite'
$taskPool   = Join-Path $storesDir 'SqliteTaskPoolStore.cs'
$snapshot   = Join-Path $storesDir 'SqliteSnapshotStore.cs'

$excludeRx      = '\\(obj|bin|BuildOutput|_deprecated|backups)\\'
$violations     = [System.Collections.Generic.List[string]]::new()
$regCallRx      = 'UsharpSqlFunction\.EnsureRegistered\(\)'
$idxCallRx      = '\.EnsureUsharpIndex\('
$inlinePathRx   = "'\$\."

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  usharp_get 集中式转换层守卫（V4/V5/V6-form）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

function Get-CsFiles([string]$Dir) {
    if (-not (Test-Path -LiteralPath $Dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $Dir -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch $excludeRx })
}

# ---------------- G1 注册单点 ----------------
$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Frame\Usharp\src'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$regSites = 0
foreach ($r in $scanRoots) {
    foreach ($f in (Get-CsFiles $r)) {
        $n = ([regex]::Matches([System.IO.File]::ReadAllText($f.FullName), $regCallRx)).Count
        if ($n -gt 0) {
            $regSites += $n
            if ($ShowDetail) { Write-Host ("  [G1] {0} x{1}" -f $f.Name, $n) -ForegroundColor DarkGray }
        }
    }
}
if ($regSites -ne [int]$baseline.EnsureRegisteredCallSites.Value) {
    $violations.Add("G1 UsharpSqlFunction.EnsureRegistered() 调用点 $regSites != 基线 $([int]$baseline.EnsureRegisteredCallSites.Value)（唯一注册点 = UsharpDataPorts.EnsureSqlFunctions）")
}

# ---------------- G2 下推表达式同源（无内联路径字面量） ----------------
$inlineHits = 0
foreach ($f in @($taskPool, $snapshot)) {
    if (-not (Test-Path -LiteralPath $f)) { continue }
    $no = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f)) {
        $no++
        if ($line.TrimStart().StartsWith('//')) { continue }
        $m = ([regex]::Matches($line, $inlinePathRx)).Count
        if ($m -gt 0) {
            $inlineHits += $m
            if ($ShowDetail) { Write-Host ("  [G2] {0}:{1} {2}" -f [IO.Path]::GetFileName($f), $no, $line.Trim()) -ForegroundColor DarkGray }
        }
    }
}
if ($inlineHits -gt [int]$baseline.InlinePathLiterals.Value) {
    $violations.Add("G2 存储层内联 JSON 路径字面量 $inlineHits 处 > 基线 $([int]$baseline.InlinePathLiterals.Value)（须经 UsharpRecordPaths 单源）")
}

# ---------------- G3 表达式索引落点（路径常量源） ----------------
$idxSites = 0
$idxBad   = 0
foreach ($f in @($taskPool, $snapshot)) {
    if (-not (Test-Path -LiteralPath $f)) { continue }
    foreach ($line in [System.IO.File]::ReadAllLines($f)) {
        if ($line.TrimStart().StartsWith('//')) { continue }
        $n = ([regex]::Matches($line, $idxCallRx)).Count
        if ($n -eq 0) { continue }
        $idxSites += $n
        if ($line -notmatch 'UsharpRecordPaths\.') { $idxBad += $n }
    }
}
if ($idxSites -ne [int]$baseline.UsharpIndexSites.Value) {
    $violations.Add("G3 usharp_get 表达式索引落点 $idxSites != 基线 $([int]$baseline.UsharpIndexSites.Value)（TaskPool 2 + Snapshot 1）")
}
if ($idxBad -gt 0) {
    $violations.Add("G3 索引落点 $idxBad 处路径参数非 UsharpRecordPaths（索引与查询表达式会漂移 ⇒ 下推不命中）")
}

# ---------------- G4 CAS 单语句（读改写由 WHERE 守卫原子） ----------------
$casSites = 0
if (Test-Path -LiteralPath $taskPool) {
    $tp = [System.IO.File]::ReadAllText($taskPool)
    $hasSet    = $tp -match 'SET \{KvUsharpTable\.ColDoc\}=@d'
    $hasFrom   = $tp -match '=@from'
    $hasStatus = $tp -match 'UsharpRecordPaths\.Status'
    if ($hasSet -and $hasFrom -and $hasStatus) { $casSites = 1 }
}
if ($casSites -ne [int]$baseline.CasSingleStatementSites.Value) {
    $violations.Add("G4 单语句 CAS 落点 $casSites != 基线 $([int]$baseline.CasSingleStatementSites.Value)（SET [Doc]=@d ... usharp_get(Status)=@from）")
}

# ---------------- G5 正/负对照（防假绿） ----------------
if (-not (Test-Path -LiteralPath $taskPool)) { $violations.Add('G5 正对照失效：SqliteTaskPoolStore.cs 缺失（判据无标的）') }
if (-not ('UsharpSqlFunction.EnsureRegistered();' -match $regCallRx)) { $violations.Add('G5 负对照失效：注册点判据未命中合成样本 (ANTI-FALSE-GREEN)') }
if (-not ("'`$.Status'" -match $inlinePathRx)) { $violations.Add('G5 负对照失效：内联路径判据未命中合成样本 (ANTI-FALSE-GREEN)') }
if (-not ('AND usharp_get([Doc],''$`$.Status'')=@from' -match '=@from')) { $violations.Add('G5 负对照失效：CAS 判据未命中合成样本 (ANTI-FALSE-GREEN)') }

# ---------------- 汇总 ----------------
if ($ShowDetail) {
    Write-Host ("  [明细] 注册点={0}；内联路径={1}；索引落点={2}（非法 {3}）；CAS={4}" -f `
        $regSites, $inlineHits, $idxSites, $idxBad, $casSites) -ForegroundColor DarkGray
}

foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Red }

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ('[OK] usharp_get 集中式转换层：注册单点 / 表达式同源 / 索引 {0} 落点 / 单语句 CAS' -f $idxSites) -ForegroundColor Green
    exit 0
}

if ($Check) { exit 1 }
exit 0
