# Check-KeyCorpusGolden.ps1
# KB1 语料门禁（阻断级）：golden 键语料结构/覆盖/边界样本齐备。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    # 机器可读出口（P2/P4 聚合消费）：无计数基线，actual 为空表但保持统一载荷。
    [switch]$Json,
    [switch]$EmitActual
)

$ErrorActionPreference = 'Stop'
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name KeyCorpusGolden

$fixture = Join-Path $Root 'Environment\CentralTest\AgentTest\Agent.Modules.Tests\NodeCarriers\Fixtures\keys.golden.json'
$requiredFamilies = @($baseline.RequiredFamilies.Value)
$requiredDisplay = @($baseline.RequiredDisplayFamilies.Value)
$boundary = @($baseline.BoundarySamples.Value)
$minimum = [int]$baseline.MinimumEntries.Value
$allowedSources = @('old-impl', 'declaration', 'sampled', 'manual')

$violations = [System.Collections.Generic.List[string]]::new()

function Get-CorpusViolations {
    param($Entries, [string[]]$Families, [string[]]$DisplayFamilies, [string[]]$Boundaries, [int]$Min)
    $out = [System.Collections.Generic.List[string]]::new()
    $items = @($Entries)
    if ($items.Count -lt $Min) { $out.Add("语料条目不足：actual=$($items.Count), min=$Min") }

    foreach ($entry in $items) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.family)) { $out.Add('条目缺 family'); continue }
        if ([string]::IsNullOrWhiteSpace([string]$entry.surface)) { $out.Add("$($entry.family) 缺 surface") }
        if ([string]::IsNullOrWhiteSpace([string]$entry.rendered)) { $out.Add("$($entry.family) 缺 rendered") }
        if ($allowedSources -notcontains [string]$entry.source) { $out.Add("$($entry.family) 未知 source: $($entry.source)") }
    }

    foreach ($family in $Families) {
        $rows = @($items | Where-Object { [string]$_.family -eq $family -and $_.valid -ne $false })
        if ($rows.Count -eq 0) { $out.Add("族未覆盖：$family") ; continue }
        if ($DisplayFamilies -contains $family -and
            @($rows | Where-Object { [string]$_.surface -eq 'Display' }).Count -eq 0) {
            $out.Add("Display 面未覆盖：$family")
        }
    }

    $rendered = @($items | ForEach-Object { [string]$_.rendered })
    foreach ($sample in $Boundaries) {
        if ($rendered -notcontains $sample) { $out.Add("边界样本缺失：$sample") }
    }

    return $out
}

if (-not (Test-Path -LiteralPath $fixture)) {
    $violations.Add("语料文件缺失：$fixture")
    $entries = @()
} else {
    $document = Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json
    $entries = @($document.entries)
}

$found = @(Get-CorpusViolations -Entries $entries -Families $requiredFamilies `
    -DisplayFamilies $requiredDisplay -Boundaries $boundary -Min $minimum)
if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }

if ($NegativeControl) {
    $probe = @($entries | Where-Object { [string]$_.family -ne $requiredFamilies[0] })
    $probeViolations = @(Get-CorpusViolations -Entries $probe -Families $requiredFamilies `
        -DisplayFamilies $requiredDisplay -Boundaries $boundary -Min $minimum)
    if ($probeViolations.Count -eq 0) { $violations.Add('负对照失败：缺族样本未被判红') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyCorpusGolden' -Json:$Json -EmitActual:$EmitActual
