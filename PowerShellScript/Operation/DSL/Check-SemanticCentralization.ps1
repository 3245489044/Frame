#requires -Version 5.1
<#
.SYNOPSIS
    语义集中守卫（SemanticCentralization）—— 防止边类型词汇退回各域自实现。

.DESCRIPTION
    基线 = Baselines\SemanticCentralization.psd1。
    SC1 收口类下限：`class ReferenceEdgeTypes` 定义点 ≥ 基线（防单源被删而门禁恒绿）。
    SC2 域内字面量直接赋值上限：非豁免域、非唯一源内，边类型字面量直接赋值的文件数 ≤ 基线（只减不增）。

    模式：默认报告模式；-Check 校验模式（违归 → 退出码 1）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违归 → 退出码 1。

.PARAMETER ShowDetail
    输出逐命中点明细。
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
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# 基线单源 = Baselines\SemanticCentralization.psd1（禁内联数值基线；守卫 = Operation\DSL\Check-GateBaselines.ps1）
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name SemanticCentralization

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  语义集中守卫（边类型词汇单源 / 域内只减不增）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$csRoot = Join-Path $Root 'Frame\CsharpPrj'
$allCs = @(Get-ChildItem -Path $csRoot -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\' })

# SC1 收口类定义点
$sc1Expected = [int]$baseline.ReferenceEdgeTypesDefs.Value
$sc1Hits = @($allCs | Select-String -Pattern 'class ReferenceEdgeTypes' -SimpleMatch)
$sc1Actual = $sc1Hits.Count
Write-Host ("  SC1 收口类定义点：实测 {0} / 基线 {1}" -f $sc1Actual, $sc1Expected) -ForegroundColor Gray
if ($sc1Actual -lt $sc1Expected)
{
    $violations.Add("[SC1] 收口类定义点 $sc1Actual < 基线 $sc1Expected ⇒ 边类型唯一源被删/未注册")
}
if ($ShowDetail)
{
    foreach ($h in $sc1Hits) { Write-Host ("    {0}:{1}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber) -ForegroundColor DarkGray }
}

# SC2 域内字面量直接赋值（非豁免、非唯一源）
$sc2Expected = [int]$baseline.EdgeTypeLiteralSites.Value
$exemptLayers = @($baseline.ExemptLayers.Value)
$canonical = [string]$baseline.CanonicalSource.Value
$literals = @($baseline.ScannedLiterals.Value)

$sc2Hits = @()
foreach ($lit in $literals)
{
    $pattern = '"' + $lit + '"'
    $h = $allCs | Select-String -Pattern $pattern -SimpleMatch |
        Where-Object { $_.Line -match '=\s*"' }
    foreach ($m in $h)
    {
        $rel = $m.Path -replace [regex]::Escape($Root + '\'), ''
        $relNorm = $rel -replace '/', '\'
        $isExempt = $false
        foreach ($e in $exemptLayers) { if ($relNorm -like ('*' + $e + '*')) { $isExempt = $true; break } }
        $isCanonical = $relNorm -ieq $canonical
        if (-not $isExempt -and -not $isCanonical) { $sc2Hits += $m }
    }
}
$sc2Actual = ($sc2Hits | Select-Object -Unique Path).Count
Write-Host ("  SC2 域内字面量直接赋值（非豁免/非唯一源）：实测 {0} 文件 / 基线 {1}" -f $sc2Actual, $sc2Expected) -ForegroundColor Gray
if ($sc2Actual -gt $sc2Expected)
{
    $violations.Add("[SC2] 域内字面量直接赋值 $sc2Actual > 基线 $sc2Expected ⇒ 新增散乱边类型字面量（须别名转发到 ReferenceEdgeTypes）")
}
if ($ShowDetail)
{
    foreach ($h in ($sc2Hits | Select-Object -Unique Path))
{
        Write-Host ("    {0}" -f ($h -replace [regex]::Escape($Root + '\'), '')) -ForegroundColor DarkGray
    }
}

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 语义集中守卫：0 违归（边类型词汇单源在位、域内无散乱字面量）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 语义集中守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
