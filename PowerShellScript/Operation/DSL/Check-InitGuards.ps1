#requires -Version 5.1
<#
.SYNOPSIS
    初始化护栏门禁（InitGuards）—— 「空值护栏收敛到 PortGuard.Required」由纪律变为机器判据（P1）。
.DESCRIPTION
    基线 = Baselines\InitGuards.psd1（单源；禁内联数值基线）。
    G1 `throw new ArgumentNullException`：实测 ≤ 上限（只减不增）。
    G2 `ArgumentNullException.ThrowIfNull`：实测 ≤ 上限（只减不增）。
    G3 `PortGuard.Required`：实测 ≥ 下限（只增不减；防门面被删/回退）。
    G4 正/负对照（防判据空转）：合成样本必被同一正则捕获；PortGuard 样本不被旧形正则误捕。
    模式：默认报告模式；-Check 校验模式（违规/对照失败 → 退出码 1）。
.PARAMETER Root
    仓库超根（缺省 = 本脚本上溯四级；含 Frame\ 与 Projects\ 两个子模块）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
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

# 基线单源 = Baselines\InitGuards.psd1（禁内联数值基线；守卫 = Operation\DSL\Check-GateBaselines.ps1）
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name InitGuards

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  初始化护栏门禁（PortGuard.Required 收敛 / 旧形只减不增）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 扫描面 = Frame\CsharpPrj ∪ Projects\Plug\Agent\src（与 Check-FallbackInventory 同口径）
$scanRoots = [System.Collections.Generic.List[string]]::new()
$frameRoot = Join-Path $Root 'Frame\CsharpPrj'
if (Test-Path -LiteralPath $frameRoot) { $scanRoots.Add($frameRoot) }
$agentRoot = Join-Path $Root 'Projects\Plug\Agent\src'
if (Test-Path -LiteralPath $agentRoot) { $scanRoots.Add($agentRoot) }

$allCs = @()
foreach ($r in $scanRoots)
{
    $allCs += @(Get-ChildItem -LiteralPath $r -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|_deprecated|BuildOutput|backups)\\' })
}

# 正则单源（G1/G2/G3 与 G4 对照共用同一口径）
$rxArgNullThrow = 'throw\s+new\s+ArgumentNullException'
$rxThrowIfNull  = 'ArgumentNullException\.ThrowIfNull'
$rxPortGuard    = 'PortGuard\.Required'

$g1 = @($allCs | Select-String -Pattern $rxArgNullThrow)
$g2 = @($allCs | Select-String -Pattern $rxThrowIfNull)
$g3 = @($allCs | Select-String -Pattern $rxPortGuard)

$b1 = [int]$baseline.ArgumentNullExceptionThrow.Value
$b2 = [int]$baseline.ThrowIfNull.Value
$b3 = [int]$baseline.PortGuardRequired.Value

Write-Host ("  G1 throw new ArgumentNullException：实测 {0} / 上限 {1}" -f $g1.Count, $b1) -ForegroundColor Gray
Write-Host ("  G2 ArgumentNullException.ThrowIfNull：实测 {0} / 上限 {1}" -f $g2.Count, $b2) -ForegroundColor Gray
Write-Host ("  G3 PortGuard.Required：实测 {0} / 下限 {1}" -f $g3.Count, $b3) -ForegroundColor Gray

if ($g1.Count -gt $b1) { $violations.Add("[G1] throw new ArgumentNullException 实测 $($g1.Count) > 上限 $b1 ⇒ 新增旧形空值护栏（须改用 PortGuard.Required）") }
if ($g2.Count -gt $b2) { $violations.Add("[G2] ArgumentNullException.ThrowIfNull 实测 $($g2.Count) > 上限 $b2 ⇒ 新增旧形空值护栏（须改用 PortGuard.Required）") }
if ($g3.Count -lt $b3) { $violations.Add("[G3] PortGuard.Required 实测 $($g3.Count) < 下限 $b3 ⇒ 门面用量回退（T 出口被删/回退？）") }

if ($ShowDetail)
{
    foreach ($h in $g1) { Write-Host ("    [G1] {0}:{1}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber) -ForegroundColor DarkGray }
    foreach ($h in $g2) { Write-Host ("    [G2] {0}:{1}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber) -ForegroundColor DarkGray }
}

# G4 正/负对照（防判据空转）：合成样本必中，PortGuard 样本不得被旧形正则误捕
$selfTestFail = 0
if ('if (x == null) throw new ArgumentNullException(nameof(x));' -notmatch $rxArgNullThrow) {
    $selfTestFail++; Write-Host "  [G4] 正对照失败：ArgumentNullException 样本未被捕获" -ForegroundColor Red
}
if ('ArgumentNullException.ThrowIfNull(x);' -notmatch $rxThrowIfNull) {
    $selfTestFail++; Write-Host "  [G4] 正对照失败：ThrowIfNull 样本未被捕获" -ForegroundColor Red
}
if ('PortGuard.Required(x, nameof(x));' -match $rxArgNullThrow -or 'PortGuard.Required(x, nameof(x));' -match $rxThrowIfNull) {
    $selfTestFail++; Write-Host "  [G4] 负对照失败：PortGuard 样本被旧形正则误捕" -ForegroundColor Red
}
if ($selfTestFail -eq 0) { Write-Host "  G4 正/负对照：通过" -ForegroundColor Gray }

if ($violations.Count -eq 0 -and $selfTestFail -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 初始化护栏门禁：0 违规（旧形只减不增 / PortGuard.Required 只增不减 / 对照通过）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host ("[FAIL] 初始化护栏门禁命中 {0} 处：" -f ($violations.Count + $selfTestFail)) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
