<#
.SYNOPSIS
    启动 trace 门禁（Gate=LaunchTrace）—— K4 失败可复算：每次启动必留可复算日志。

.DESCRIPTION
    判据载体 = 契约 Trace 表 + 实跑产物（结构事实；禁读执行者叙述）：
      K1 每份契约声明 Trace.Dir / Tee(stdout,stderr) / KeepDays>0
      K2 执行体含 trace 写入锚点（trace_version=1）
      K3 实跑 `--diagnose --no-pause` 后新增日志存在，且含全部 LogMarkers（头部可复算 + 阶段行 + 判定证据）
      K4 实跑耗时 ≤ MaxSeconds
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..\..\..\Share\Launch\LaunchContract.psm1') -Force -DisableNameChecking

$baseline = Import-GateBaseline -Name LaunchTrace
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动 trace 守卫（LaunchTrace / K4 失败可复算）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[string]]::new()

$executor = Join-Path $repoRoot ([string]$baseline.ExecutorRelative.Value)
if (-not (Test-Path -LiteralPath $executor)) {
    Write-Host ("  [FAIL] 执行体缺失: {0}" -f $executor) -ForegroundColor Red
    exit 2
}

$contracts = @(Get-LaunchContractList -RepoRoot $repoRoot)
if ($contracts.Count -eq 0) {
    Write-Host '  [FAIL] 无契约（trace 声明面缺失）' -ForegroundColor Red
    exit 2
}

$executorText = [System.IO.File]::ReadAllText($executor)
if ($executorText -notmatch 'trace_version=1') {
    $violations.Add("[K2] 执行体缺 trace 头部锚点 trace_version=1")
}

foreach ($file in $contracts) {
    $contract = Import-LaunchContract -Path $file.FullName
    $trace = $contract['Trace']
    if ($trace -isnot [System.Collections.IDictionary]) {
        $violations.Add(("[K1] {0}: 未声明 Trace" -f $file.Name))
        continue
    }
    if ([string]::IsNullOrWhiteSpace([string]$trace['Dir'])) {
        $violations.Add(("[K1] {0}: Trace.Dir 缺省" -f $file.Name))
    }
    if ([int]$trace['KeepDays'] -le 0) {
        $violations.Add(("[K1] {0}: Trace.KeepDays 须 > 0（只增不丢）" -f $file.Name))
    }
    foreach ($stream in @('stdout', 'stderr')) {
        if (@($trace['Tee']) -notcontains $stream) {
            $violations.Add(("[K1] {0}: Trace.Tee 缺 '{1}'（原文保留）" -f $file.Name, $stream))
        }
    }
}

# 实跑断言：--diagnose 也必须落日志（A3 的根因：失败没有日志）
$firstContract = Import-LaunchContract -Path $contracts[0].FullName
$product = [string]$firstContract['Product']
$traceRel = [string]$firstContract['Trace']['Dir']
$traceRoot = Join-Path (Join-Path $repoRoot $traceRel) $product

$before = @{}
if (Test-Path -LiteralPath $traceRoot) {
    foreach ($f in @(Get-ChildItem -LiteralPath $traceRoot -Filter '*.log' -File -ErrorAction SilentlyContinue)) { $before[$f.FullName] = $true }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $executor -Product $product -RawArgs @($baseline.DiagnoseArgs.Value) | Out-Null
$rc = if ($null -eq $LASTEXITCODE) { 2 } else { [int]$LASTEXITCODE }
$sw.Stop()

if ($rc -ne 0) { $violations.Add(("[K3] 诊断实跑未通过（exit={0}）；该次失败必须有日志可复算" -f $rc)) }
if ($sw.Elapsed.TotalSeconds -gt [double]$baseline.MaxSeconds.Value) {
    $violations.Add(("[K4] 诊断实跑耗时 {0:N1}s > {1}s" -f $sw.Elapsed.TotalSeconds, [double]$baseline.MaxSeconds.Value))
}

$newLogs = @()
if (Test-Path -LiteralPath $traceRoot) {
    $newLogs = @(Get-ChildItem -LiteralPath $traceRoot -Filter '*.log' -File -ErrorAction SilentlyContinue | Where-Object { -not $before.ContainsKey($_.FullName) })
}
if ($newLogs.Count -eq 0) {
    $violations.Add("[K3] 诊断实跑未新增 trace 日志（失败将不可复算）")
}
else {
    $latest = $newLogs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $content = Get-Content -LiteralPath $latest.FullName -Raw -Encoding UTF8
    foreach ($marker in @($baseline.LogMarkers.Value)) {
        if ($content -notmatch [regex]::Escape([string]$marker)) {
            $violations.Add(("[K3] trace 缺标记 '{0}'（{1}）" -f [string]$marker, $latest.Name))
        }
    }
    Write-Host ("  最近 trace: {0}" -f $latest.FullName) -ForegroundColor Gray
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] trace 可复算（头部 + 阶段 + 判定证据齐备）' -ForegroundColor Green
exit 0
