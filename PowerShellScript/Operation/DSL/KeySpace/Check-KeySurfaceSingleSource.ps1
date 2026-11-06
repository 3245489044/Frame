# Check-KeySurfaceSingleSource.ps1
# GP-1 键面单源（棘轮）：历史行键候选表 / 行键解析实现只允许一份（键面投影 + 声明 readOnly）。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name KeySurfaceSingleSource

# 判据（结构事实）：每项 = 正则 × 全仓计数；期望值只来自基线（棘轮只降不升）。
$criteria = [ordered]@{
    'candidate-row-keys'   = 'CandidateRowKeys\s*\('
    'history-row-key-impl' = 'IReadOnlyList<string>\s+HistoryRowKeys\s*\('
    'prefix-concat'        = '"history:"\s*\+|HistoryTypePrefix\s*\+'
}

$expected = @{}
foreach ($section in @($baseline.Value, $baseline.Debt)) {
    if ($null -eq $section) { continue }
    foreach ($row in @($section.Value)) {
        if ($row -is [string]) { $expected[$row] = 0; continue }
        $key = [string]$row['Key']
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        $expected[$key] = if ($row.Contains('Count')) { [int]$row['Count'] } else { 0 }
    }
}

$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$exclude = 'Modules[\\/]KeySpace[\\/]|Modules[\\/]Parsing[\\/]Keys[\\/]|[\\/](?:obj|bin|BuildOutput)[\\/]|[\\/](?:Tests?|\.Tests)[\\/]'

function Get-Counts([string]$Text) {
    $clean = [regex]::Replace($Text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $clean = [regex]::Replace($clean, '//[^\r\n]*', '')
    $map = @{}
    foreach ($key in $criteria.Keys) {
        $map[$key] = ([regex]::Matches($clean, $criteria[$key])).Count
    }
    return $map
}

$actual = @{}
foreach ($key in $criteria.Keys) { $actual[$key] = 0 }
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-GateFiles -Root $scanRoot -Extension cs)) {
        $rel = $file.Substring($Root.Length + 1).Replace('\', '/')
        if ($rel -match $exclude) { continue }
        $text = Get-Content -LiteralPath $file -Raw
        if ($null -eq $text) { continue }
        $counts = Get-Counts $text
        foreach ($key in $criteria.Keys) { $actual[$key] += [int]$counts[$key] }
    }
}

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($key in $criteria.Keys) {
    $cap = if ($expected.ContainsKey($key)) { [int]$expected[$key] } else { 0 }
    if ([int]$actual[$key] -gt $cap) {
        $violations.Add("$key 超基线：actual=$($actual[$key]), baseline=$cap（键面/候选表只允许一处实现）")
    }
}

if ($NegativeControl) {
    $sample = 'private static IReadOnlyList<string> CandidateRowKeys(HistoryKey key)'
    if ([int](Get-Counts $sample)['candidate-row-keys'] -eq 0) {
        $violations.Add('负对照失败：候选表样本未被判据命中')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeySurfaceSingleSource'
