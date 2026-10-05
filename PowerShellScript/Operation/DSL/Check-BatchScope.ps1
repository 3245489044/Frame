#requires -Version 5.1
<#
.SYNOPSIS
    批次授权单守卫（BatchScope）—— 把 [批次声明] 从「自述」升级为「对结构核验」（过程层缺陷 P1/P2/P6）。

.DESCRIPTION
    根因（修复文档 §1-A/§2）：批次入口不可判定、越界编辑无判据、批次声明为自述 —— 三者同形 =
    「本批该做什么 / 事实是什么」在跨阶段没有「单一生产者 + 单一可判定面」。
    本件把该语义收敛为一处：机器可读授权单（batches.json）+ 对结构（实际改动文件集）核验。

    判据（B1–B3）：
      B1 越界：实际改动文件集 ∖ (活动批 allowSet ∪ derived ∪ preexisting) = 越界清单（非空即违规）。
      B2 否决：实际改动文件集 ∩ denySet（非 preexisting 者）= 禁改清单（非空即违规）。
      B3 正/负对照（防假绿）：匹配器对合成样本须「入面命中 ∧ 越分隔符不命中 ∧ 越界样本不被误收」；
         活动批 allowSet 模式数须 ≥ 基线下限。

    载体（结构事实）：`git status --porcelain` 的改动文件集，跨 Main + 子模块（.gitmodules）聚合；
      子模块指针行（Frame / Projects）不计入（其内部改动逐仓枚举后加前缀）。
    灰度回退（修复文档 §7）：授权单缺件 / 无活动批 / 无 git ⇒ 只告警不失败（默认 warn；稳定后转判据）。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。
    依赖方向（硬约束）：本件只读仓库（含只读 git）；零侧写。

.PARAMETER Manifest
    授权单路径（仓库相对或绝对）；缺省取 Baselines\BatchScope.psd1 的 Manifest 事实。
.PARAMETER Batch
    被核验批（batchId，可多值）；缺省取授权单 active 列表（并集）。
.PARAMETER Changed
    直接给定改动文件集（跳过 git 采集；供正/负对照 / 合成样本）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER ShowDetail
    列出全部分类明细（入面 / 派生 / 既有 / 越界 / 禁改）。
#>
[CmdletBinding()]
param(
    [string]$Manifest = '',
    [string[]]$Batch = @(),
    [string[]]$Changed = @(),
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name BatchScope

$violations = [System.Collections.Generic.List[string]]::new()
$notes      = [System.Collections.Generic.List[string]]::new()

# ---------------- 工具：glob → 正则（/ 为分隔符；大小写不敏感；`**` 跨段、`*` 限段） ----------------
function Convert-GlobToRegex {
    param([Parameter(Mandatory = $true)][string]$Glob)
    $g = ($Glob -replace '\\', '/')
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append('^')
    $i = 0
    while ($i -lt $g.Length) {
        $c = $g[$i]
        if ($c -eq '*') {
            if (($i + 1) -lt $g.Length -and $g[$i + 1] -eq '*') {
                if (($i + 2) -lt $g.Length -and $g[$i + 2] -eq '/') { [void]$sb.Append('(?:[^/]+/)*'); $i += 3 }
                else { [void]$sb.Append('.*'); $i += 2 }
            }
            else { [void]$sb.Append('[^/]*'); $i += 1 }
        }
        elseif ($c -eq '?') { [void]$sb.Append('[^/]'); $i += 1 }
        else { [void]$sb.Append([regex]::Escape([string]$c)); $i += 1 }
    }
    [void]$sb.Append('$')
    return $sb.ToString()
}

function Test-GlobMatch {
    param([string]$Path, [string[]]$Globs)
    $p = ($Path -replace '\\', '/')
    foreach ($glob in $Globs) {
        if ([string]::IsNullOrWhiteSpace($glob)) { continue }
        if ([regex]::IsMatch($p, (Convert-GlobToRegex -Glob $glob), [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) { return $true }
    }
    return $false
}

# ---------------- 工具：跨仓改动文件集（结构事实；只读 git） ----------------
function Get-SubmodulePaths {
    $mods = [System.Collections.Generic.List[string]]::new()
    $gitmodules = Join-Path $repoRoot '.gitmodules'
    if (Test-Path -LiteralPath $gitmodules) {
        foreach ($line in [IO.File]::ReadAllLines($gitmodules)) {
            $m = [regex]::Match($line, '^\s*path\s*=\s*(?<p>.+?)\s*$')
            if ($m.Success) { $mods.Add(($m.Groups['p'].Value -replace '/', '\')) }
        }
    }
    return $mods.ToArray()
}

function Get-StatusPaths {
    param([Parameter(Mandatory = $true)][string]$Repo, [string[]]$Ignore = @())
    $out = [System.Collections.Generic.List[string]]::new()
    $raw = @(& git -C $Repo -c core.quotepath=false status --porcelain 2>$null)
    foreach ($line in $raw) {
        if ($null -eq $line) { continue }
        $s = [string]$line
        if ($s.Length -lt 4) { continue }
        $path = $s.Substring(3).Trim()
        if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1].Trim() }
        $path = $path.Trim('"') -replace '/', '\'
        if ($Ignore -contains $path) { continue }
        $out.Add($path)
    }
    return $out.ToArray()
}

# ---------------- 1) 载入授权单 ----------------
$manifestPath = $Manifest
if ([string]::IsNullOrWhiteSpace($manifestPath)) { $manifestPath = [string]$baseline.Manifest.Value }
if (-not [IO.Path]::IsPathRooted($manifestPath)) { $manifestPath = Join-Path $repoRoot $manifestPath }

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  批次授权单守卫（BatchScope：改动集 ⊆ 授权面）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Host "[WARN] 授权单缺件（灰度 warn，不失败）：$manifestPath" -ForegroundColor Yellow
    Write-Host '  [OK] BatchScope 跳过（未启用授权单）' -ForegroundColor Green
    exit 0
}

$mf = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$batchList = @($mf.batches)

# ---------------- 2) 选择批（批次须被选择，非文档首项；人当前指令 > 计划文档） ----------------
$selectedIds = @()
if ($Batch.Count -gt 0) { $selectedIds = @($Batch) }
elseif ($mf.PSObject.Properties['active']) { $selectedIds = @($mf.active) }
if ($selectedIds.Count -eq 0) {
    Write-Host '[WARN] 授权单无 active 批（灰度 warn，不失败）' -ForegroundColor Yellow
    Write-Host '  [OK] BatchScope 跳过（未选择批）' -ForegroundColor Green
    exit 0
}

$activeBatches = @($batchList | Where-Object { $selectedIds -contains [string]$_.batchId })
foreach ($id in $selectedIds) {
    if (-not ($batchList | Where-Object { [string]$_.batchId -eq [string]$id })) {
        $violations.Add("[B0] 授权单无此批：$id（batchId 拼写漂移）")
    }
}

$allowGlobs = @(); $denyGlobs = @()
foreach ($b in $activeBatches) {
    if ($b.PSObject.Properties['allowSet']) { $allowGlobs += @($b.allowSet) }
    if ($b.PSObject.Properties['denySet'])  { $denyGlobs  += @($b.denySet) }
}
$derivedGlobs = @(); if ($mf.PSObject.Properties['derived'])    { $derivedGlobs = @($mf.derived) }
$preGlobs     = @(); if ($mf.PSObject.Properties['preexisting']){ $preGlobs     = @($mf.preexisting) }
if ($mf.PSObject.Properties['denySet']) { $denyGlobs += @($mf.denySet) }
$allowGlobs = @($allowGlobs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

# ---------------- 3) 实际改动文件集（结构事实；跨 Main + 子模块聚合） ----------------
if ($Changed.Count -gt 0) {
    $actual = @($Changed | ForEach-Object { ([string]$_ -replace '/', '\').TrimStart('\') })
}
else {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Host '[WARN] 未找到 git（无法取改动集）⇒ 灰度 warn，不失败' -ForegroundColor Yellow
        Write-Host '  [OK] BatchScope 跳过（无 git）' -ForegroundColor Green
        exit 0
    }
    $submodules = Get-SubmodulePaths
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($p in (Get-StatusPaths -Repo $repoRoot -Ignore $submodules)) { $list.Add($p) }
    foreach ($sub in $submodules) {
        $subAbs = Join-Path $repoRoot $sub
        if (-not (Test-Path -LiteralPath $subAbs)) { continue }
        foreach ($p in (Get-StatusPaths -Repo $subAbs)) { $list.Add(($sub + '\' + $p)) }
    }
    $actual = @($list | Sort-Object -Unique)
}

# ---------------- 4) 分类（single-source：pre > deny > allow > derived > 越界） ----------------
$inScope = @(); $derived = @(); $pre = @(); $outOfScope = @(); $denied = @()
foreach ($f in $actual) {
    if (Test-GlobMatch -Path $f -Globs $preGlobs) { $pre += $f; continue }
    if (Test-GlobMatch -Path $f -Globs $denyGlobs) { $denied += $f; continue }
    if (Test-GlobMatch -Path $f -Globs $allowGlobs) { $inScope += $f; continue }
    if (Test-GlobMatch -Path $f -Globs $derivedGlobs) { $derived += $f; continue }
    $outOfScope += $f
}

foreach ($f in $outOfScope) { $violations.Add("[B1] 越界改动（不在活动授权面）：$f") }
foreach ($f in $denied)     { $violations.Add("[B2] 禁改面改动（denySet）：$f") }

# ---------------- 5) 正/负对照（防假绿：匹配器自证 + 授权面下限） ----------------
$posGlob = 'Frame/PowerShellScript/**'
$posPath = 'Frame/PowerShellScript/Operation/DSL/Check-BatchScope.ps1'
if (-not (Test-GlobMatch -Path $posPath -Globs @($posGlob))) {
    $violations.Add('[B3] 正对照失效：匹配器未把入面样本判为命中（glob 被改坏）')
}
$negSegPath = 'Frame/PowerShellScript/Operation/DSL/sub/deep.ps1'
if (Test-GlobMatch -Path $negSegPath -Globs @('Frame/PowerShellScript/Operation/DSL/*.ps1')) {
    $violations.Add('[B3] 负对照失效：`*` 越分隔符误命中（glob 过宽）')
}
$negProbe = 'Docs/__scope_probe__/probe.md'
if (Test-GlobMatch -Path $negProbe -Globs $allowGlobs) {
    $violations.Add('[B3] 负对照失效：越界样本被活动授权面误收（授权面过宽）')
}
$minAllow = [int]$baseline.MinAllowPatterns.Value
if ($allowGlobs.Count -lt $minAllow) {
    $violations.Add("[B3] 正对照失效：活动批允许面模式 $($allowGlobs.Count) < 基线 $minAllow（授权单被掏空）")
}

# ---------------- 6) 报告 ----------------
$manifestRel = if ($manifestPath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) { $manifestPath.Substring($repoRoot.Length).TrimStart('\') } else { $manifestPath }
Write-Host ("授权单: {0}" -f $manifestRel) -ForegroundColor Gray
Write-Host ("活动批: {0} | 改动文件: {1} | 入面 {2} / 派生 {3} / 既有 {4} / 越界 {5} / 禁改 {6}" -f `
        ($selectedIds -join ', '), $actual.Count, $inScope.Count, $derived.Count, $pre.Count, $outOfScope.Count, $denied.Count) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($pair in @(@{T='入面'; L=$inScope}, @{T='派生'; L=$derived}, @{T='既有'; L=$pre}, @{T='越界'; L=$outOfScope}, @{T='禁改'; L=$denied})) {
        foreach ($f in $pair.L) { Write-Host ("  [{0}] {1}" -f $pair.T, $f) -ForegroundColor DarkGray }
    }
}
foreach ($n in $notes) { Write-Host "  ! $n" -ForegroundColor Yellow }

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] BatchScope：0 违规（改动集 ⊆ 授权面 ∪ 派生 ∪ 既有；正/负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] BatchScope 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：把改动纳入活动批 allowSet（并重跑授权单），或回退越界改动；denySet 面一律禁改' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0
