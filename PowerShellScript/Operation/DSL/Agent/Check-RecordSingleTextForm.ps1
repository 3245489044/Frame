#requires -Version 5.1
<#
.SYNOPSIS
    记录单一文本形态守卫（RecordSingleTextForm）—— 详情态只保留整记录编辑器 JSON，无平行载荷文本。

.DESCRIPTION
    判据（B3/RED-06）：
      G1 零平行文本：Agent src 内 `DetailPayloadJson` 引用 = 0（载荷视图改由记录态派生）。
      G2 记录态在位：`DetailRecordJson` 在位。
      G3 正/负对照：合成样本（DetailPayloadJson 引用）必被捕获。

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
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  记录单一文本形态守卫（RecordSingleTextForm）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$src = Join-Path $Root 'Projects\Plug\Agent\src'
$refs = 0
$hasRecord = $false
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $c = Get-Count $t 'DetailPayloadJson'
    if ($c -gt 0) { $refs += $c; $violations.Add("[G1] DetailPayloadJson 引用：$($f.Name) × $c") }
    if (Get-Count $t 'DetailRecordJson' -gt 0) { $hasRecord = $true }
}
if (-not $hasRecord) { $violations.Add('[G2] 未见 DetailRecordJson（记录态缺失）') }

# 正/负对照
if ((Get-Count 'state.DetailPayloadJson = x;' 'DetailPayloadJson') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（DetailPayloadJson 引用）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 记录单一文本形态通过（正负对照绿）' -ForegroundColor Green
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
