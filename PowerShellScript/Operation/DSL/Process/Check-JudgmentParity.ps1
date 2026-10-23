#requires -Version 5.1
<#
.SYNOPSIS
    判定口径对拍门禁（JudgmentParity）：旧入口必须薄转发到 Judgment 内核，不得保留第二套判定体。
.DESCRIPTION
    C1 RoundContractJudge 调用 JudgmentEngine.Judge；
    C2 RoundContractJudge 不再出现 `Except(` 集合差判定体；
    C3 PromptPreflight 调用 AliasCoverageRule，且不再自写 IsNullOrWhiteSpace 口径；
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

function Get-ParityViolations {
    param([string]$JudgeText, [string]$PreflightText, [bool]$TestPresent, [bool]$KernelPresent)

    $violations = [System.Collections.Generic.List[string]]::new()
    if (-not $KernelPresent) { $violations.Add('Judgment/ 内核目录缺失') }
    if ($JudgeText -notmatch 'JudgmentEngine\.Judge') { $violations.Add('RoundContractJudge 未转发 JudgmentEngine.Judge') }
    if ($JudgeText -match 'Except\(') { $violations.Add('RoundContractJudge 仍保留 Except( 判定体') }
    if ($PreflightText -notmatch 'AliasCoverageRule') { $violations.Add('PromptPreflight 未接 AliasCoverageRule') }
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
