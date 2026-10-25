#requires -Version 5.1
<#
.SYNOPSIS
    判定口径对拍门禁（JudgmentParity）：旧入口必须薄转发到 Judgment 内核，不得保留第二套判定体。
.DESCRIPTION
    C1 RoundContractJudge 调用 JudgmentEngine.Judge；
    C2 RoundContractJudge 不再出现 `Except(` 集合差判定体；
    C3 PromptPreflight 走统一判据（TextJudge 或 AliasCoverageRule），且不再自写 IsNullOrWhiteSpace 口径；
    C4 对拍测试文件在位；
    C5 负对照：含 `Except(` 的合成样本必须被判违规。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name JudgmentParity

function Get-ParityViolations {
    param([string]$JudgeText, [string]$PreflightText, [bool]$TestPresent, [bool]$KernelPresent)

    $violations = [System.Collections.Generic.List[string]]::new()
    if (-not $KernelPresent) { $violations.Add('Judgment/ 内核目录缺失') }
    if ($JudgeText -notmatch 'JudgmentEngine\.Judge') { $violations.Add('RoundContractJudge 未转发 JudgmentEngine.Judge') }
    if ($JudgeText -match 'Except\(') { $violations.Add('RoundContractJudge 仍保留 Except( 判定体') }
    if ($PreflightText -notmatch 'TextJudge|AliasCoverageRule') { $violations.Add('PromptPreflight 未走统一判据（TextJudge/AliasCoverageRule）') }
    if ($PreflightText -match 'IsNullOrWhiteSpace') { $violations.Add('PromptPreflight 仍自写 IsNullOrWhiteSpace 口径') }
    if (-not $TestPresent) { $violations.Add('JudgmentParityTests.cs 不在位') }
    return $violations
}

$judgeFile = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Execution\Contracts\RoundContractJudge.cs'
$preflightFile = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Execution\PromptPreflight.cs'
$testFile = Join-Path $Root 'Environment\CentralTest\CSharpTest\Operation.Tests\HierarchyMethod.Tests\Judgment\JudgmentParityTests.cs'
$kernelDir = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod\Judgment'

$judgeText = if (Test-Path -LiteralPath $judgeFile) { Get-Content -LiteralPath $judgeFile -Raw } else { '' }
$preflightText = if (Test-Path -LiteralPath $preflightFile) { Get-Content -LiteralPath $preflightFile -Raw } else { '' }
$testPresent = Test-Path -LiteralPath $testFile
$kernelPresent = Test-Path -LiteralPath $kernelDir

if ($NegativeControl) {
    $probe = Get-ParityViolations -JudgeText 'var x = declared.Except(produced);' -PreflightText '' -TestPresent $false -KernelPresent $false
    if ($probe.Count -eq 0) {
        Write-Output 'FAIL JudgmentParity: 负对照未命中（检测器恒真）'
        exit 1
    }
    Write-Output "PASS JudgmentParity (negative control: $($probe.Count) violations)"
    exit 0
}

$violations = Get-ParityViolations -JudgeText $judgeText -PreflightText $preflightText -TestPresent $testPresent -KernelPresent $kernelPresent

# 基线棘轮（只降不增；下限=正对照，防判据无标的）
$kernelWeight = if ($kernelPresent) { 1 } else { 0 }
$exceptBodies = ([regex]::Matches($judgeText, 'Except\(')).Count
$localJudges = ([regex]::Matches($preflightText, 'IsNullOrWhiteSpace')).Count
if ($kernelWeight -lt [int]$g.KernelPresent.Value) { $violations.Add("[C1] Judgment 内核在位数 $kernelWeight < 基线 $([int]$g.KernelPresent.Value)") }
if ($exceptBodies -gt [int]$g.LegacyExceptBodies.Value) { $violations.Add("[C2] Except( 判定体 $exceptBodies 处 > 基线 $([int]$g.LegacyExceptBodies.Value)（只降不增）") }
if ($localJudges -gt [int]$g.PreflightLocalPresence.Value) { $violations.Add("[C3] PromptPreflight 自写 IsNullOrWhiteSpace $localJudges 处 > 基线 $([int]$g.PreflightLocalPresence.Value)（只降不增）") }

if ($ShowDetail) {
    Write-Output "Judge=$judgeFile"
    Write-Output "Preflight=$preflightFile"
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output "  - $_" }
    Write-Output "FAIL JudgmentParity: $($violations.Count) violation(s)"
    if ($Check) { exit 1 }
    exit 0
}

Write-Output 'PASS JudgmentParity'
exit 0
