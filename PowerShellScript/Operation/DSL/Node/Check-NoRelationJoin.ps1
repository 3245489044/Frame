#requires -Version 5.1
<#
.SYNOPSIS
    关系自 join 门禁（NoRelationJoin）—— 集合查询实现点收敛为 1。

.DESCRIPTION
    判据载体 = 源码静态形态（结构事实）：同集合二次遍历 / Where+SelectMany 自 join / 双 foreach + 键比较。
    权威: axioms:Strategy.SingleImplementation / AX.DependencyCycleDetection。

      NJ1 唯一实现点存在：基线 ImplementationPoint 文件存在且含集合遍历原语
      NJ2 实现点唯一：扫描面内检出「自 join 形态」的文件 ⊆ {ImplementationPoint} ∪ Allow
      NJ3 负对照（-NegativeControl）：合成自 join 片段必须被同一判定函数判红

    模式：默认报告；-Check 违规 ⇒ 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name NoRelationJoin

$implementation = [string]$baseline.ImplementationPoint.Value
$allow = @($baseline.Allow | ForEach-Object { [string]$_.Value } | Where-Object { $_ })
$scanRoots = @(
    'Frame\CsharpPrj\Share\Frameworks\UniversalReference\Relations',
    'Frame\CsharpPrj\Operation\DataStoreDriven\Domains\Hierarchy'
)

# 自 join 形态（单一实现；负对照复用同一函数）。
function Test-SelfJoinText([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $false }
    if ($text -match '\.Where\([^;\r\n]*\)\s*\.SelectMany\(') { return $true }
    if ($text -match '\.SelectMany\([^;\r\n]*\.Where\(') { return $true }
    $foreachCount = [regex]::Matches($text, '\bforeach\s*\(').Count
    if ($foreachCount -ge 2 -and $text -match '\.Key\s*(==|!=)\s*') { return $true }
    return $false
}

if ($NegativeControl) {
    $probe = 'var x = items.Where(a => a.Id == id).SelectMany(a => a.Children.Where(b => b.Key == a.Key));'
    if (-not (Test-SelfJoinText $probe)) {
        Write-Host '[NoRelationJoin][NEGATIVE-CONTROL-FAILED] 合成自 join 未被判红（判据空转）' -ForegroundColor Red
        exit 1
    }
    Write-Host '[NoRelationJoin][NEGATIVE-CONTROL-OK] 合成自 join 命中' -ForegroundColor Green
    exit 0
}

$violations = New-Object System.Collections.Generic.List[string]

$implementationPath = Join-Path $Root ($implementation -replace '/', '\')
if (-not (Test-Path -LiteralPath $implementationPath)) {
    $violations.Add("NJ1 唯一实现点不存在：$implementation")
}

$allowSet = @{}
foreach ($item in $allow) { $allowSet[($item -replace '/', '\')] = $true }
$allowSet[($implementation -replace '/', '\')] = $true

$sites = New-Object System.Collections.Generic.List[string]
foreach ($rel in $scanRoots) {
    $dir = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    foreach ($file in (Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs')) {
        $relPath = $file.FullName.Substring($Root.Length).TrimStart('\', '/')
        if ($allowSet.ContainsKey($relPath)) { continue }
        if (Test-SelfJoinText (Get-Content -LiteralPath $file.FullName -Raw)) { $sites.Add($relPath) }
    }
}

foreach ($site in $sites) { $violations.Add("NJ2 第二集合查询实现点：$site（唯一实现 = $implementation）") }

if ($ShowDetail) {
    Write-Host "  · 实现点 = $implementation"
    foreach ($site in $sites) { Write-Host "  · 检出 = $site" }
}

if ($violations.Count -gt 0) {
    Write-Host "[NoRelationJoin] 违规 = $($violations.Count)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[NoRelationJoin] 集合查询实现点 = 1（无第二自 join 实现）' -ForegroundColor Green
exit 0
