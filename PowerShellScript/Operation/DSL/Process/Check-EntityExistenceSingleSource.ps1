# Check-EntityExistenceSingleSource.ps1
# GP-2 实体存在性单源：存在性谓词实现归注册表；禁直读探针与命令层重复前置检查。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }
$baseline = Import-GateBaseline -Name EntityExistenceSingleSource

# 判据（结构事实）：实现点数只来自基线（棘轮只降不升）。
$criteria = [ordered]@{
    'direct-probe'          = 'class\s+StoreEntityExistenceProbe'
    'existence-impl'        = ':\s*IEntityExistenceProbe'
    'duplicate-precheck'    = '_\s*tasks\s*\.\s*ExistsAsync|_tasks\s*\.\s*ExistsAsync'
    'unmapped-visible'      = 'public\s+bool\s+IsMapped\s*\(\s*string\s+entityType'
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
$exclude = '[\\/](?:obj|bin|BuildOutput)[\\/]|[\\/](?:Tests?|\.Tests)[\\/]'

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
        $violations.Add("$key 超基线：actual=$($actual[$key]), baseline=$cap（每实体类型存在性实现=1）")
    }
}

if ($NegativeControl) {
    $sample = 'public sealed class StoreEntityExistenceProbe : IEntityExistenceProbe'
    if ([int](Get-Counts $sample)['direct-probe'] -eq 0) {
        $violations.Add('负对照失败：直读探针样本未被判据命中')
    }
    if ([int](Get-Counts 'if (!await _tasks.ExistsAsync(cmd.Task))')['duplicate-precheck'] -eq 0) {
        $violations.Add('负对照失败：命令层重复检查样本未被判据命中')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'EntityExistenceSingleSource'
