#requires -Version 5.1
<#
.SYNOPSIS
    边盖章门禁（EdgeStamp）—— C8：全库 `origin_flow` 为空的边 = 0。

.DESCRIPTION
    判据载体 = 回填工具导出的结构事实报告（Data\Tests\edge-stamp\latest.json）；
    执行者叙述不参与。权威: axioms:SEP.JudgmentCarrierStructural / formula:SM.EffectiveFaceAssertion。

      ES1 报告存在（缺失 ⇒ 判据不可判定，fail-closed）
      ES2 报告 schema 合法且计数字段齐备
      ES3 missing（origin_flow 为空）> 0 ⇒ 红
      ES4 负对照（-NegativeControl）：合成未盖章报告必须被判红

    模式：默认报告；-Check 违规 ⇒ 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$ReportPath = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name EdgeStamp
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $Root ([string]$baseline.ReportPath.Value -replace '/', '\')
}

function Test-StampReport($report) {
    $problems = New-Object System.Collections.Generic.List[string]
    if ($null -eq $report) { $problems.Add('ES1 回填报告为空'); return $problems }
    if ([string]$report.schema -ne 'edge-stamp-report/v1') { $problems.Add("ES2 报告 schema 非法：$($report.schema)") }
    foreach ($field in @('total', 'missing', 'patched')) {
        if ($null -eq $report.$field) { $problems.Add("ES2 报告缺计数字段：$field") }
    }
    if ($null -ne $report.missing -and [int]$report.missing -gt 0) {
        $problems.Add("ES3 origin_flow 为空的边 = $($report.missing)（判据 = 0）")
    }
    return $problems
}

if ($NegativeControl) {
    $probe = [pscustomobject]@{ schema = 'edge-stamp-report/v1'; total = 3; missing = 1; patched = 0 }
    $probeProblems = @(Test-StampReport $probe)
    if (-not ($probeProblems | Where-Object { $_ -like 'ES3 *' })) {
        Write-Host '[EdgeStamp][NEGATIVE-CONTROL-FAILED] 合成未盖章边未被判红（判据空转）' -ForegroundColor Red
        exit 1
    }
    Write-Host "[EdgeStamp][NEGATIVE-CONTROL-OK] 合成未盖章报告命中 $($probeProblems.Count) 项" -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $ReportPath)) {
    Write-Host "[EdgeStamp] 回填报告不存在：$ReportPath（先跑 edge-stamp-repair -DryRun -OutFile …）" -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
$violations = @(Test-StampReport $report)

if ($ShowDetail) {
    Write-Host "  · total=$($report.total) missing=$($report.missing) patched=$($report.patched) apply=$($report.apply)"
}

if ($violations.Count -gt 0) {
    Write-Host "[EdgeStamp] 违规 = $($violations.Count)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[EdgeStamp] origin_flow 为空的边 = 0（C8）' -ForegroundColor Green
exit 0
