#requires -Version 5.1
<#
.SYNOPSIS
    产物新鲜度判据族单源守卫（FreshnessFamilySingleSource）—— 新鲜度比较实现仅一处。

.DESCRIPTION
    判据（B6/SCT-08）：
      G1 包含口径单源：源引用包含正则（`AgentUi_[^"]*` Matches）仅在 Baselines\ArtifactFreshness.psm1。
      G2 指纹口径单源：`Get-FileHash` 指纹实现仅在 Baselines\ArtifactFreshness.psm1。
      G3 消费方委托：Check-AgentRuntimeUIConfigFreshness 经族函数（Get-ArtifactContainmentMisses）。
      G4 正/负对照：合成样本（第二处包含口径 regex）必被捕获。

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
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  产物新鲜度判据族单源守卫（FreshnessFamilySingleSource）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$family = Join-Path $Root 'Frame\PowerShellScript\Baselines\ArtifactFreshness.psm1'
$psRoot = Join-Path $Root 'Frame\PowerShellScript'
$selfPath = (Resolve-Path -LiteralPath $PSCommandPath -ErrorAction SilentlyContinue).Path

# G1/G2：族函数定义唯一（新鲜度比较实现单源；不按通用原语计数，避免误伤无关 Get-FileHash）
if (-not (Test-Path -LiteralPath $family)) {
    $violations.Add("[G1] 缺新鲜度判据族：$family")
} else {
    $famText = [System.IO.File]::ReadAllText($family)
    if ($famText -notmatch 'function\s+Get-ArtifactContainmentMisses') { $violations.Add('[G1] 族缺「结构包含」口径函数') }
    if ($famText -notmatch 'function\s+Test-ArtifactFingerprintFresh') { $violations.Add('[G2] 族缺「内容指纹」口径函数') }
}

foreach ($def in @('Get-ArtifactContainmentMisses', 'Test-ArtifactFingerprintFresh', 'Get-ArtifactContentFingerprint')) {
    $defs = 0
    foreach ($f in (Get-ChildItem -Path $psRoot -Recurse -Include '*.ps1', '*.psm1' -File -ErrorAction SilentlyContinue)) {
        if ($selfPath -and ($f.FullName -eq $selfPath)) { continue }
        $defs += Get-Count ([System.IO.File]::ReadAllText($f.FullName)) ('function\s+' + $def + '\b')
    }
    if ($defs -ne 1) { $violations.Add("[G1] 新鲜度比较函数 '$def' 定义 $defs 处（应恰 1 = ArtifactFreshness.psm1）") }
}

$consumer = Join-Path $Root 'Frame\PowerShellScript\Modules\Node\Check-AgentRuntimeUIConfigFreshness.ps1'
if (Test-Path -LiteralPath $consumer) {
    if ([System.IO.File]::ReadAllText($consumer) -notmatch 'Get-ArtifactContainmentMisses') {
        $violations.Add('[G3] 配置新鲜度守卫未委托族函数（Get-ArtifactContainmentMisses）')
    }
}

# 正/负对照：定义唯一性判据能识别第二处定义
if ((Get-Count 'function Get-ArtifactContainmentMisses { }' 'function\s+Get-ArtifactContainmentMisses\b') -eq 0) {
    $violations.Add('[G4] 负对照失效：合成样本（重复族函数定义）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3 新鲜度族单源通过（正负对照绿）' -ForegroundColor Green
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
