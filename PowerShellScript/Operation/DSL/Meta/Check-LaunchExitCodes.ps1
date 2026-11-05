<#
.SYNOPSIS
    启动退出码门禁（Gate=LaunchExitCodes）—— K3 退出码可证：0 只来自进程结构事实。

.DESCRIPTION
    判据载体 = 契约 ExitCodes 表 × 执行体常量退出点（结构事实）：
      K1 执行体不得出现常量 `exit 0`（0 不作默认值）
      K2 执行体每个常量退出码 ⊆ 契约 ExitCodes
      K3 契约每个非零退出码 ⊆ 执行体常量退出码（声明不得悬空）
      K4 执行体须含生命周期锚点 WaitForExit( 且以进程退出码返回（exit $processExitCode）
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..\..\..\Share\Launch\LaunchContract.psm1') -Force -DisableNameChecking

$baseline = Import-GateBaseline -Name LaunchExitCodes
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..\..'))

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  启动退出码守卫（LaunchExitCodes / K3 退出码可证）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[string]]::new()

$executor = Join-Path $repoRoot ([string]$baseline.ExecutorRelative.Value)
if (-not (Test-Path -LiteralPath $executor)) {
    Write-Host ("  [FAIL] 执行体缺失: {0}" -f $executor) -ForegroundColor Red
    exit 2
}

$contracts = @(Get-LaunchContractList -RepoRoot $repoRoot)
if ($contracts.Count -eq 0) {
    Write-Host '  [FAIL] 无契约（退出码无声明面）' -ForegroundColor Red
    exit 2
}

$declared = [System.Collections.Generic.List[int]]::new()
foreach ($file in $contracts) {
    $contract = Import-LaunchContract -Path $file.FullName
    foreach ($k in @($contract['ExitCodes'].Keys)) {
        $code = [int]$k
        if (-not $declared.Contains($code)) { $declared.Add($code) }
    }
}

$text = [System.IO.File]::ReadAllText($executor)

if ([regex]::IsMatch($text, [string]$baseline.ZeroLiteralPattern.Value)) {
    $violations.Add('[K1] 执行体含常量 exit 0（0 必须由 S6 的进程结构事实产生）')
}

$literals = @([regex]::Matches($text, [string]$baseline.LiteralExitPattern.Value) |
    ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique)

foreach ($code in $literals) {
    if (-not $declared.Contains($code)) {
        $violations.Add(("[K2] 执行体常量退出码 {0} 未在契约 ExitCodes 声明" -f $code))
    }
}
foreach ($code in @($declared)) {
    if ($code -ne 0 -and $literals -notcontains $code) {
        $violations.Add(("[K3] 契约声明退出码 {0} 在执行体中无常量退出点（悬空声明）" -f $code))
    }
}
if ($text -notmatch [regex]::Escape([string]$baseline.WaitAnchor.Value)) {
    $violations.Add(("[K4] 执行体缺生命周期锚点 '{0}'" -f [string]$baseline.WaitAnchor.Value))
}
if ($text -notmatch [regex]::Escape([string]$baseline.ProcessExitAnchor.Value)) {
    $violations.Add(("[K4] 执行体未以进程退出码返回（期望 '{0}'）" -f [string]$baseline.ProcessExitAnchor.Value))
}

Write-Host ("  契约退出码: {0}；执行体常量退出码: {1}" -f ($declared -join ','), ($literals -join ',')) -ForegroundColor Gray
Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 退出码可证（契约 ⇔ 常量退出点，0 仅来自进程）' -ForegroundColor Green
exit 0
