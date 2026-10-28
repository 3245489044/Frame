#requires -Version 5.1
<#
.SYNOPSIS
    图库索引声明守卫（GraphIndexDeclaration）—— 索引建立点只减不增（声明化过渡的棘轮）。

.DESCRIPTION
    判据（结构事实 = 计数）：
      GI1 ReferenceGraphCoordinator.Initialize() 内 EnsureIndex( 调用数 ≤ 基线（只降不升）；
      GI2 边身份唯一索引必须在位（idx_graph_edge_identity，unique: true）。
    负对照：合成一处新增 EnsureIndex( 必须判红。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name GraphIndexDeclaration
$expected = [int]$baseline.EnsureIndexCount.Value

$coordinator = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Graph\ReferenceGraphCoordinator.cs'
if (-not (Test-Path $coordinator)) { Write-Host "[FAIL] 协调器缺失：$coordinator" -ForegroundColor Yellow; exit 1 }
$text = Get-Content -LiteralPath $coordinator -Raw

function Get-IndexViolations([string]$source) {
    $out = [System.Collections.Generic.List[string]]::new()
    $count = [regex]::Matches($source, '\.EnsureIndex\(').Count
    if ($count -gt $expected) { $out.Add("GI1 EnsureIndex 计数 = $count（基线 $expected；只降不升）") }
    if ($source -notmatch 'idx_graph_edge_identity') { $out.Add('GI2 边身份唯一索引缺失（idx_graph_edge_identity）') }
    return $out
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  图库索引声明守卫（GraphIndexDeclaration）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ("  GI1 EnsureIndex 基线：{0}" -f $expected) -ForegroundColor Gray

$violations = Get-IndexViolations $text

if ($NegativeControl) {
    $sample = $text -replace 'private void Initialize\(\)', "private void Initialize()`n    { _edges.EnsureIndex(`"synthetic`");"
    $probe = Get-IndexViolations $sample
    if (@($probe | Where-Object { $_ -like 'GI1*' }).Count -gt 0) {
        Write-Host "  [OK] 负对照：新增 EnsureIndex 即判红" -ForegroundColor Green
        exit 0
    }
    Write-Host "  [FAIL] 负对照未命中（GI1 判据恒真）" -ForegroundColor Yellow
    exit 1
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] GraphIndexDeclaration：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] GraphIndexDeclaration 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
exit 1
