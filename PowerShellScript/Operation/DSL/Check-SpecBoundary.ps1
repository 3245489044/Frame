#requires -Version 5.1
<#
.SYNOPSIS
    spec 生成/手写边界守卫（SpecBoundary）—— R0 边界冻结的机器判据。

.DESCRIPTION
    背景（方案 §1.3 / §4.1）：声明面（手写真源）与生成面（机器产物）的边界此前靠约定，
    「源码树是否混入生成物」无机器事实。本件把边界落成可判定的结构事实。

    判据（载体 = 结构事实：文件存在 / 计数 / 模式匹配；禁执行者自述）：
      A 源码树零生成物：扫描面内 生成物文件名模式（基线 GeneratedPatterns）命中数 = 0，手写遗留白名单除外；
      B 清单非空：手写白名单模式数 ≥ 基线 MinHandwrittenPatterns；生成根清单 ≥ 1；
      C 生成根可见化：统计生成根内产物数（不拦，供可见化）；
      U 正/负对照：判据函数对合成样本行为正确（防判据被改坏 ⇒ 空转）。

    模式：默认报告模式（不设退出码）；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐条实测落点。
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
$b = Import-GateBaseline -Name SpecBoundary

$violations = [System.Collections.Generic.List[string]]::new()

# 扫描面：spec 相关源码根（生成根 BuildOutput 在遍历中被显式排除）。
$scanRoots = @(
    (Join-Path $Root 'Environment\CentralTest')
    (Join-Path $Root 'Frame\CsharpPrj\Strategy\Observability')
    (Join-Path $Root 'Projects\Plug\Agent\src')
)

$genPatterns = @($b.GeneratedPatterns.Value)
$allowLegacy = @($b.HandwrittenLegacyJson.Value)
$minPatterns = [int]$b.MinHandwrittenPatterns.Value

# 判据函数（单点实现：实测与正/负对照共用同一函数）。
function Test-SpecGeneratedArtifact {
    param([Parameter(Mandatory)][string]$Name)
    foreach ($p in $genPatterns) {
        if ($Name -like $p) { return $true }
    }
    return $false
}

# ---- 判据 A：源码树零生成物 ----
$hits = [System.Collections.Generic.List[string]]::new()
$legacyHits = 0
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    $candidates = Get-ChildItem -LiteralPath $scanRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\BuildOutput\\|\\obj\\|\\bin\\|\\_deprecated\\' }

    foreach ($f in $candidates) {
        if (-not (Test-SpecGeneratedArtifact -Name $f.Name)) { continue }
        $rel = $f.FullName.Substring($Root.Length).TrimStart('\')
        $isAllowed = $false
        foreach ($a in $allowLegacy) { if ($rel -eq $a) { $isAllowed = $true; break } }
        if ($isAllowed) { $legacyHits++; continue }
        $hits.Add($rel)
    }
}
foreach ($h in $hits) {
    $violations.Add("[A] 源码树出现 spec 生成物（应落生成根）：$h")
}

# ---- 判据 B：清单非空（防判据无标的假绿） ----
$patternCount = @($b.HandwrittenPatterns.Value).Count
if ($patternCount -lt $minPatterns) {
    $violations.Add("[B] 手写白名单模式数 $patternCount < 下限 $minPatterns（判据无标的假绿）")
}
if (@($b.GeneratedRoots.Value).Count -lt 1) {
    $violations.Add('[B] 生成根清单为空（判据无标的假绿）')
}

# ---- 判据 C：生成根产物可见化（不拦） ----
$generatedCount = 0
foreach ($r in @($b.GeneratedRoots.Value)) {
    $abs = Join-Path $Root $r
    if (-not (Test-Path -LiteralPath $abs)) { continue }
    $generatedCount += @(Get-ChildItem -LiteralPath $abs -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { Test-SpecGeneratedArtifact -Name $_.Name }).Count
}

# ---- 正/负对照（判据自证） ----
$posOk = (Test-SpecGeneratedArtifact -Name 'FooSpecTests.g.cs') -and
         (Test-SpecGeneratedArtifact -Name 'SpecPorts.g.cs') -and
         (Test-SpecGeneratedArtifact -Name 'foo.spec.json')
$negOk = (-not (Test-SpecGeneratedArtifact -Name 'foo.specs')) -and
         (-not (Test-SpecGeneratedArtifact -Name 'FooTests.cs'))
if (-not $posOk) { $violations.Add('[U] 正对照失败：合成生成物样本未被捕获（判据函数被改坏）') }
if (-not $negOk) { $violations.Add('[U] 负对照失败：非生成物样本被误捕') }

# ---- 报告 ----
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  spec 生成/手写边界守卫（SpecBoundary）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  扫描面：$($scanRoots.Count) 根" -ForegroundColor Gray
Write-Host "  源码树生成物：$($hits.Count) 处；手写遗留白名单命中：$legacyHits 处" -ForegroundColor Gray
Write-Host "  生成根产物（.spec.json / *.g.cs）：$generatedCount 件" -ForegroundColor Gray
Write-Host "  手写白名单模式：$patternCount（下限 $minPatterns）；生成根 $(@($b.GeneratedRoots.Value).Count) 个" -ForegroundColor Gray

if ($ShowDetail -and $hits.Count -gt 0) {
    Write-Host '  --- 违规落点 ---' -ForegroundColor DarkGray
    foreach ($h in $hits) { Write-Host "    - $h" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] SpecBoundary：0 违规（源码树零生成物 / 清单非空 / 正负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] SpecBoundary 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
