#requires -Version 5.1
<#
.SYNOPSIS
    前置开销守卫（PreflightCost）—— 验证前置（事实索引 / 对齐指纹 / 烘焙新鲜度）不得回退为「每次全量重算」。

.DESCRIPTION
    根因（框架级缺陷 · 因4）：每次运行都付固定前置开销 —— 事实索引被四处独立全扫（同一批 *.specs / *.cs 读 3–4 遍）；
    对齐检查对全量文件重算 SHA256（3577 源 + 112 dll）；invoke-verify 对任何产品改动无条件全量 bake。

    判据（接线面 / 结构事实；只读仓库，零运行）：
      P0 负对照：判据载体（源码命中计数器）对合成样本行为正确（防判据恒真/恒假）。
      P1 单源扫描：运行器内对 `*.specs` 的递归扫描调用 ≤ 1 处，且出现 `Get-TestInventory`（唯一数据面消费）。
      P2 增量指纹接线：`Get-ContentFingerprint` 定义含 `CachePath` 参数，且 `Test-ArtifactAlignment` 的两处调用均传 `-CachePath`。
      P3 烘焙新鲜度接线：`Invoke-Verify.ps1` 含 `Test-CsharpBakeFresh` 且存在「已新鲜 ⇒ 跳过 bake」分支。
      P4 锁分层接线：运行器对 `-NoBuild` 相位不得取门（含 `Test-BuildGateBusy` 只读探针）。

    模式：默认报告模式；-Check 校验模式（违规 → 退出码 1）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐判据实测明细。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

$centralTest = Join-Path $repoRoot 'Environment\CentralTest'
$runnerPath = Join-Path $centralTest 'Internal\Run-CSharpTests.ps1'
$verifyPath = Join-Path $centralTest 'Internal\Invoke-Verify.ps1'

$violations = [System.Collections.Generic.List[string]]::new()

foreach ($p in @($runnerPath, $verifyPath)) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Error "判据标的缺失：$p"; exit 2 }
}
$runnerText = Get-Content -LiteralPath $runnerPath -Raw
$verifyText = Get-Content -LiteralPath $verifyPath -Raw

# ── 判据载体：源码命中计数（唯一定义点；P0 正/负对照共用同一计数器）──
function Get-PatternHitCount([string]$Text, [string]$Pattern) {
    if ([string]::IsNullOrEmpty($Text)) { return 0 }
    return [regex]::Matches([string]$Text, $Pattern).Count
}

# ── P0 正/负对照 ──
if ((Get-PatternHitCount -Text 'aaa foo bbb foo' -Pattern 'foo') -ne 2) {
    $violations.Add('[P0] 正对照失效：命中计数器未把合成样本判为 2（判据载体被改坏）')
}
if ((Get-PatternHitCount -Text 'aaa' -Pattern 'foo') -ne 0) {
    $violations.Add('[P0] 负对照失效：缺失模式未判 0（判据载体恒真）')
}

# ── P1 单源扫描 ──
$specScanHits = Get-PatternHitCount -Text $runnerText -Pattern "Get-ChildItem[^\r\n]*-Filter\s+'\*\.specs'"
if ($ShowDetail) { Write-Host ("  P1 运行器内 *.specs 递归扫描处 = {0}" -f $specScanHits) -ForegroundColor DarkGray }
if ($specScanHits -gt 1) {
    $violations.Add("[P1] 运行器内仍有 $specScanHits 处 *.specs 独立扫描（应 ≤ 1：数据面单源 = Get-TestInventory）")
}
if ((Get-PatternHitCount -Text $runnerText -Pattern 'Get-TestInventory') -lt 1) {
    $violations.Add('[P1] 运行器未消费 Get-TestInventory（事实索引未收敛到单源）')
}

# ── P2 增量指纹接线 ──
if ((Get-PatternHitCount -Text $runnerText -Pattern '\[string\]\$CachePath = ''\''') -lt 1) {
    $violations.Add('[P2] Get-ContentFingerprint 缺 CachePath 参数（增量哈希缓存未接线）')
}
$cacheCalls = Get-PatternHitCount -Text $runnerText -Pattern 'Get-ContentFingerprint\s+[^\r\n]*-CachePath'
if ($ShowDetail) { Write-Host ("  P2 传 -CachePath 的指纹调用处 = {0}" -f $cacheCalls) -ForegroundColor DarkGray }
if ($cacheCalls -lt 2) {
    $violations.Add("[P2] Test-ArtifactAlignment 的指纹调用未全部传 -CachePath（实测 $cacheCalls < 2）")
}

# ── P3 烘焙新鲜度短路接线 ──
if ((Get-PatternHitCount -Text $verifyText -Pattern 'Test-CsharpBakeFresh') -lt 2) {
    $violations.Add('[P3] Invoke-Verify 未接入 Test-CsharpBakeFresh（bake 新鲜度短路缺失）')
}
if ((Get-PatternHitCount -Text $verifyText -Pattern '跳过（权威产物全新鲜') -lt 1) {
    $violations.Add('[P3] Invoke-Verify 的 bake 分支无「已新鲜 ⇒ 跳过」分支')
}

# ── P4 锁分层接线 ──
if ((Get-PatternHitCount -Text $runnerText -Pattern 'Test-BuildGateBusy') -lt 1) {
    $violations.Add('[P4] 运行器未接入只读探针 Test-BuildGateBusy（-NoBuild 相位仍会排队取门）')
}
if ((Get-PatternHitCount -Text $verifyText -Pattern 'Exit-BuildGate') -lt 1) {
    $violations.Add('[P4] Invoke-Verify 未在构建相位后释放构建门（门仍覆盖 bake→test→gate）')
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] PreflightCost：0 违规（事实索引单源 ∧ 指纹增量 ∧ bake 新鲜度短路 ∧ 锁分层接线）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] PreflightCost 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：事实索引单源 = Get-TestInventory；对齐指纹增量 = Get-ContentFingerprint -CachePath；bake 前先判新鲜度；构建门只覆盖构建相位。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0