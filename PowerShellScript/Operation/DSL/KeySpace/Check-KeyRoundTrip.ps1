# Check-KeyRoundTrip.ps1
# KB1 往返门禁（阻断级）：golden 语料覆盖每族可写面，且往返测试在册。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateBaseline.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }
$baseline = Import-GateBaseline -Name KeyRoundTrip
$requiredFamilies = @($baseline.RequiredFamilies.Value)
$minimum = [int]$baseline.MinimumEntries.Value

$fixture = Join-Path $Root 'Environment\CentralTest\AgentTest\Agent.Modules.Tests\NodeCarriers\Fixtures\keys.golden.json'
$testFile = Join-Path $Root 'Environment\CentralTest\AgentTest\Agent.Modules.Tests\NodeCarriers\KeyRoundTripTests.cs'
$violations = [System.Collections.Generic.List[string]]::new()

function Get-RoundTripViolations {
    param($Entries, [string[]]$Families, [int]$Min)
    $out = [System.Collections.Generic.List[string]]::new()
    $items = @($Entries | Where-Object { $_.valid -ne $false -and $_.prefix -ne $true })
    if ($items.Count -lt $Min) { $out.Add("可往返条目不足：actual=$($items.Count), min=$Min") }
    foreach ($family in $Families) {
        if (@($items | Where-Object { [string]$_.family -eq $family }).Count -eq 0) {
            $out.Add("族无可往返条目：$family")
        }
    }
    return $out
}

$entries = @()
if (-not (Test-Path -LiteralPath $fixture)) {
    $violations.Add("语料文件缺失：$fixture")
} else {
    $entries = @((Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json).entries)
}
$found = @(Get-RoundTripViolations -Entries $entries -Families $requiredFamilies -Min $minimum)
if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }

if (-not (Test-Path -LiteralPath $testFile)) {
    $violations.Add("往返测试缺失：$testFile")
} else {
    $text = Get-Content -LiteralPath $testFile -Raw
    if ($text -notmatch 'class\s+KeyRoundTripTests') { $violations.Add('往返测试类未在册：KeyRoundTripTests') }
    if ($text -notmatch 'TryParse') { $violations.Add('往返测试未调用 TryParse') }
    if ($text -notmatch 'Render') { $violations.Add('往返测试未调用 Render') }
}

if ($NegativeControl) {
    $probeViolations = @(Get-RoundTripViolations -Entries @() -Families $requiredFamilies -Min $minimum)
    if ($probeViolations.Count -eq 0) { $violations.Add('负对照失败：空语料未被判红') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyRoundTrip'
