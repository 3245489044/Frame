#requires -Version 5.1
<#
.SYNOPSIS
    判定点单源门禁（JudgmentPoints）：内核在位、判据定义点收敛、动作抛点收敛。
.DESCRIPTION
    C1 Judgment/ 内核目录与五个一等抽象文件在位；
    C2 Rules/ 规则文件 >= 5；
    C3 `IReadOnlyList<string> MissingPorts(` 定义点 = 1（薄转发）；
    C4 `throw new RoundFailureException` 抛点 = 2（执行失败 + JudgmentEngine.Apply）；
    C5 负对照：合成 0 定义/0 抛点必须被判为违规（防恒真）。
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

function Get-JudgmentPointViolations {
    param(
        [int]$MissingPortsDefinitions,
        [int]$RoundFailureThrows,
        [int]$RuleFiles,
        [bool]$KernelPresent
    )

    $violations = [System.Collections.Generic.List[string]]::new()
    if (-not $KernelPresent) { $violations.Add('Judgment/ 内核目录缺失') }
    if ($RuleFiles -lt 5) { $violations.Add("Rules/ 规则文件数=$RuleFiles（期望 >= 5）") }
    if ($MissingPortsDefinitions -ne 1) { $violations.Add("MissingPorts 定义点=$MissingPortsDefinitions（期望 1）") }
    if ($RoundFailureThrows -ne 2) { $violations.Add("RoundFailureException 抛点=$RoundFailureThrows（期望 2：执行失败 + Apply）") }
    return $violations
}

if ($NegativeControl) {
    $probe = Get-JudgmentPointViolations -MissingPortsDefinitions 0 -RoundFailureThrows 0 -RuleFiles 0 -KernelPresent $false
    if ($probe.Count -eq 0) {
        Write-Output 'FAIL JudgmentPoints: 负对照未命中（检测器恒真）'
        exit 1
    }
    Write-Output "PASS JudgmentPoints (negative control: $($probe.Count) violations)"
    exit 0
}

$judgmentDir = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod\Judgment'
$kernelPresent = Test-Path -LiteralPath $judgmentDir
$ruleFiles = 0
if ($kernelPresent) {
    $rulesDir = Join-Path $judgmentDir 'Rules'
    if (Test-Path -LiteralPath $rulesDir) {
        $ruleFiles = @(Get-ChildItem -LiteralPath $rulesDir -Filter '*.cs' -File -ErrorAction SilentlyContinue).Count
    }
}

$srcRoot = Join-Path $Root 'Frame\CsharpPrj'
$missingPorts = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File |
    Select-String -Pattern 'IReadOnlyList<string> MissingPorts(' -SimpleMatch)
$throws = @(Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter '*.cs' -File |
    Select-String -Pattern 'throw new RoundFailureException' -SimpleMatch)

$violations = Get-JudgmentPointViolations `
    -MissingPortsDefinitions $missingPorts.Count `
    -RoundFailureThrows $throws.Count `
    -RuleFiles $ruleFiles `
    -KernelPresent $kernelPresent

if ($ShowDetail) {
    $missingPorts | ForEach-Object { Write-Output ("MissingPorts: {0}:{1}" -f $_.Path, $_.LineNumber) }
    $throws | ForEach-Object { Write-Output ("Throw: {0}:{1}" -f $_.Path, $_.LineNumber) }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Output "  - $_" }
    Write-Output "FAIL JudgmentPoints: $($violations.Count) violation(s)"
    if ($Check) { exit 1 }
    exit 0
}

Write-Output 'PASS JudgmentPoints'
exit 0
