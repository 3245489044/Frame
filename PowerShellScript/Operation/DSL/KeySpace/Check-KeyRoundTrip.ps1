# Check-KeyRoundTrip.ps1
# KB1 往返门禁（阻断级）：golden 语料覆盖每族可写面，且往返测试在册。
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

function Get-CanonicalViolations {
    param($Samples)
    $out = [System.Collections.Generic.List[string]]::new()
    $items = @($Samples)
    if ($items.Count -eq 0) {
        $out.Add('语料缺少 canonical 幂等样本（canonical 段：coord/raw/expected）')
        return $out
    }
    # 派生键样本（KB1 扩展）：哈希派生前缀不得被当作业务键往返。
    if (@($items | Where-Object { $_.derived -eq $true }).Count -eq 0) {
        $out.Add('canonical 段缺少派生键样本（derived=true；判据：派生前缀不产生业务键）')
    }
    foreach ($entry in $items) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.coord)) { $out.Add('canonical 样本缺 coord') }
        if ([string]::IsNullOrWhiteSpace([string]$entry.raw)) { $out.Add("canonical 样本缺 raw：$($entry.coord)") }
        if ([string]::IsNullOrWhiteSpace([string]$entry.expected)) { $out.Add("canonical 样本缺 expected：$($entry.coord)") }
    }
    return $out
}

$entries = @()
$canonical = @()
if (-not (Test-Path -LiteralPath $fixture)) {
    $violations.Add("语料文件缺失：$fixture")
} else {
    $document = Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json
    $entries = @($document.entries)
    $canonical = @($document.canonical)
}
$found = @(Get-RoundTripViolations -Entries $entries -Families $requiredFamilies -Min $minimum)
if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }
$shape = @(Get-CanonicalViolations -Samples $canonical)
if ($shape.Count -gt 0) { $violations.AddRange([string[]]$shape) }

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
    $probeShape = @(Get-CanonicalViolations -Samples @())
    if ($probeShape.Count -eq 0) { $violations.Add('负对照失败：空 canonical 段未被判红') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyRoundTrip' -Json:$Json -EmitActual:$EmitActual
