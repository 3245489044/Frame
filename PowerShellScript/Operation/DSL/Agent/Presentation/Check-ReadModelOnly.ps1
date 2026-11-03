#Requires -Version 7.0
<#
.SYNOPSIS
    ReadModelOnly 门禁：Turn 渲染面只消费读模型（禁运行态镜像直判 / 直读存储表）。

.DESCRIPTION
    判据载体 = 结构事实（源码正则命中），非执行者叙述。
      R1 正向：扫描面内出现 TurnReadModel.Shared / ITurnReadModel（读面存在）。
      R2 反向：基线 ForbiddenPatterns 命中数 ≤ MaxHits（只减不增）。
      R3 负对照（-SelfTest）：合成违规样本必须被捕获（门禁不得空转）。

.PARAMETER SelfTest
    负对照自检：用合成文本跑一次判据，返回 0 = 能捕获违规（正常），1 = 空转。

.EXAMPLE
    pwsh Frame\PowerShellScript\Operation\DSL\Agent\Presentation\Check-ReadModelOnly.ps1
#>
[CmdletBinding()]
param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\Meta\TextOps.psm1') -Force -DisableNameChecking

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$script:RepoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$baselinePath = Join-Path $script:RepoRoot 'Frame\PowerShellScript\Baselines\Agent\ReadModelOnly.psd1'
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath

function Get-Violations([string[]]$roots, [object[]]$patterns) {
    $found = [System.Collections.Generic.List[string]]::new()
    foreach ($rel in $roots) {
        $abs = Join-Path $script:RepoRoot $rel
        if (-not (Test-Path -LiteralPath $abs)) { $found.Add("[missing] $rel"); continue }
        $raw = Get-Content -LiteralPath $abs -Raw
        # 判据只认代码面：剥离行注释（注释里说明「不再用 pool.Active」不构成读法）。
        $text = (($raw -split "`n") | ForEach-Object { (Remove-LineComment -Text $_) }) -join "`n"
        foreach ($p in $patterns) {
            $m = [regex]::Matches($text, $p.Pattern)
            if ($m.Count -gt 0) {
                $found.Add(("{0}（{1} 处，{2} 行文件）" -f $p.Id, $m.Count, $rel))
            }
        }
    }
    return ,$found
}

if ($SelfTest) {
    $sample = 'var x = pool.Active; var y = row.Fixed.Round; var z = "ur_tasks";'
    $synthetic = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $baseline.ForbiddenPatterns) {
        $m = [regex]::Matches($sample, $p.Pattern)
        if ($m.Count -gt 0) { $synthetic.Add($p.Id) }
    }
    if ($synthetic.Count -eq $baseline.ForbiddenPatterns.Count) {
        Write-Host ("[ReadModelOnly] 负对照 OK：合成违规全部捕获（{0}/{1}）" -f $synthetic.Count, $baseline.ForbiddenPatterns.Count) -ForegroundColor Green
        exit 0
    }
    Write-Host ("[ReadModelOnly] 负对照失败：仅捕获 {0}/{1} ⇒ 门禁空转" -f $synthetic.Count, $baseline.ForbiddenPatterns.Count) -ForegroundColor Red
    exit 1
}

$violations = Get-Violations -roots $baseline.ScanRoots -patterns $baseline.ForbiddenPatterns

$positive = 0
foreach ($rel in $baseline.ScanRoots) {
    $abs = Join-Path $script:RepoRoot $rel
    if (-not (Test-Path -LiteralPath $abs)) { continue }
    $text = Get-Content -LiteralPath $abs -Raw
    foreach ($anchor in $baseline.AllowedReadFace) {
        if ($text.Contains($anchor)) { $positive++; break }
    }
}

Write-Host '============================================================'
Write-Host '  ReadModelOnly 门禁（Turn 渲染面只消费读模型）'
Write-Host '============================================================'
Write-Host ("  扫描面: {0} 文件 | 正向读面命中: {1}" -f @($baseline.ScanRoots).Count, $positive) -ForegroundColor Gray

$max = [int]$baseline.MaxHits
$bad = @($violations)
if ($bad.Count -gt $max) {
    Write-Host ("  [FAIL] 镜像/直读命中 {0} > 基线 {1}：" -f $bad.Count, $max) -ForegroundColor Red
    foreach ($v in $bad) { Write-Host ("    - $v") -ForegroundColor Red }
    exit 1
}

Write-Host ("  [OK] 镜像/直读命中 {0} ≤ 基线 {1}（只减不增）" -f $bad.Count, $max) -ForegroundColor Green
exit 0
