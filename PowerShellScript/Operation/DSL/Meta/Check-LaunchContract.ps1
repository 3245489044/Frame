<#
.SYNOPSIS
    启动契约门禁（Gate=LaunchContract）—— K2 契约完备：每产品恰一份契约，字段合格，入口双向覆盖。

.DESCRIPTION
    判据载体 = 契约表项 + 文件存在性（结构事实）：
      P1 契约数 ≥ 基线（正对照；防扫描面坍缩）
      P2 执行体在位（契约有消费者，禁孤儿声明）
      K1 每份契约经 Test-LaunchContract 判定合格（字段完备 / Entries 双向映射 / 禁 BuildOutput 字面）
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..\..\..\Share\Launch\LaunchContract.psm1') -Force -DisableNameChecking

$baseline = Import-GateBaseline -Name LaunchContract
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动契约守卫（LaunchContract / K2 契约完备）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[string]]::new()

$contractDir = Join-Path $repoRoot ([string]$baseline.ContractDirRelative.Value)
$entryDir = Join-Path $repoRoot ([string]$baseline.EntryDirRelative.Value)
if (-not (Test-Path -LiteralPath $contractDir)) {
    Write-Host ("  [FAIL] 契约目录缺失: {0}" -f $contractDir) -ForegroundColor Red
    exit 2
}
if (-not (Test-Path -LiteralPath $entryDir)) {
    Write-Host ("  [FAIL] 入口目录缺失: {0}" -f $entryDir) -ForegroundColor Red
    exit 2
}

$contracts = @(Get-ChildItem -LiteralPath $contractDir -Filter ([string]$baseline.ContractFilter.Value) -File -ErrorAction SilentlyContinue | Sort-Object Name)
$minContracts = [int]$baseline.MinContracts.Value
Write-Host ("  契约: {0} 份（正对照下限 {1}）" -f $contracts.Count, $minContracts) -ForegroundColor Gray
if ($contracts.Count -lt $minContracts) {
    $violations.Add(("[P1] 契约数 {0} < 基线 {1}（禁判绿）" -f $contracts.Count, $minContracts))
}

$executor = Join-Path $repoRoot ([string]$baseline.RequiredExecutor.Value)
if (-not (Test-Path -LiteralPath $executor)) {
    $violations.Add(("[P2] 执行体不在位：{0}（契约有声明无消费者）" -f [string]$baseline.RequiredExecutor.Value))
}

foreach ($file in $contracts) {
    $contract = Import-LaunchContract -Path $file.FullName
    foreach ($x in @(Test-LaunchContract -Contract $contract -ContractPath $file.FullName -RepoRoot $repoRoot)) {
        $violations.Add(("[K1] {0}: {1}" -f $file.Name, $x))
    }
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 启动契约完备（字段完备 + 入口双向覆盖 + 零装载面路径）' -ForegroundColor Green
exit 0
