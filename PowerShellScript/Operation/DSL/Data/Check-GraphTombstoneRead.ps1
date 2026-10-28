#requires -Version 5.1
<#
.SYNOPSIS
    图边墓碑读取点守卫（GraphTombstoneRead）—— 删除意图必须有约束力（只写不读 = 删除失效）。

.DESCRIPTION
    判据（结构事实，非叙述）：
      GT1 写前裁决调用点 ≥ 1（ReferenceGraphCoordinator 内 _policy.Admit( ）；
      GT2 边墓碑读取点 ≥ 1（EdgeTombstoneExists( 调用）；
      GT3 边墓碑键构造与读取同源（GraphIdentityKeys.EdgeTombstoneKey 在协调器内被引用于判据）。
    负对照：从样本删掉 EdgeTombstoneExists( 调用后必须判红。
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

$coordinator = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Graph\ReferenceGraphCoordinator.cs'
if (-not (Test-Path $coordinator)) { Write-Host "[FAIL] 协调器缺失：$coordinator" -ForegroundColor Yellow; exit 1 }
$text = Get-Content -LiteralPath $coordinator -Raw

function Get-ReadViolations([string]$source) {
    $out = [System.Collections.Generic.List[string]]::new()
    if ([regex]::Matches($source, '_policy\.Admit\(').Count -lt 1) { $out.Add('GT1 写前裁决调用点 = 0（墓碑策略未接入写路径）') }
    if ([regex]::Matches($source, 'EdgeTombstoneExists\(').Count -lt 1) { $out.Add('GT2 边墓碑读取点 = 0（只写不读 ⇒ 已删边会被写回）') }
    if ([regex]::Matches($source, 'GraphIdentityKeys\.EdgeTombstoneKey\(').Count -lt 2) { $out.Add('GT3 边墓碑键构造/读取未同源（引用 < 2 处）') }
    return $out
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  图边墓碑读取点守卫（GraphTombstoneRead）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$violations = Get-ReadViolations $text

if ($NegativeControl) {
    $sample = [regex]::Replace($text, 'EdgeTombstoneExists\(', 'NotARead(')
    $probe = Get-ReadViolations $sample
    if (@($probe | Where-Object { $_ -like 'GT2*' }).Count -gt 0) {
        Write-Host "  [OK] 负对照：移除读取点即判红" -ForegroundColor Green
        exit 0
    }
    Write-Host "  [FAIL] 负对照未命中（GT2 判据恒真）" -ForegroundColor Yellow
    exit 1
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] GraphTombstoneRead：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] GraphTombstoneRead 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
exit 1
