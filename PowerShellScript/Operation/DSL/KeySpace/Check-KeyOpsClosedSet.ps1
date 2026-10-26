# Check-KeyOpsClosedSet.ps1
# KB2 固有函数闭集（棘轮）：键上下文内的拼接/插值/前缀判据/切片只允许经 KeyOps。
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
$baseline = Import-GateBaseline -Name KeyOpsClosedSet

$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$exclude = 'Modules[\\/]KeySpace[\\/]|Modules[\\/]Parsing[\\/]Keys[\\/]|[\\/](?:obj|bin|BuildOutput)[\\/]|[\\/](?:Tests?|\.Tests)[\\/]'
$rules = [ordered]@{
    concat = '(?:ProcessKeys|SegmentKeys|SessionViewKeys|ObservabilityKeys|ArchiveKeys|MessageStreamKeys)\.(?:FamilyPrefix|PositionKeyTemplate|IdentityTemplate)\s*\+|(?:FamilyPrefix|KeyPrefix)\s*\+\s*(?:container|session|task|round|kind|ordinal)'
    interp = '\$"[^"\r\n]*\{(?:round|seq|ordinal)\}[^"\r\n]*"'
    starts = 'StartsWith\([^)]*FamilyPrefix'
    slice  = '(?:NodeKey|KeyPrefix|FamilyPrefix)\.(?:Substring|Split)\('
}

$actual = @{}
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-GateFiles -Root $scanRoot -Extension cs)) {
        $rel = $file.Substring($Root.Length + 1).Replace('\', '/')
        if ($rel -match $exclude) { continue }
        $text = Get-Content -LiteralPath $file -Raw
        if ($null -eq $text) { continue }
        foreach ($rule in $rules.Keys) {
            $count = ([regex]::Matches($text, $rules[$rule])).Count
            if ($count -gt 0) { $actual["$rel|$rule"] = $count }
        }
    }
}

$allowed = @{}
foreach ($row in @($baseline.Value.Value)) {
    if ($row -is [string]) { $allowed[$row] = 1; continue }
    $key = [string]$row['Key']; if ([string]::IsNullOrWhiteSpace($key)) { continue }
    $allowed[$key] = if ($row.Contains('Count')) { [int]$row['Count'] } else { 1 }
}

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($key in $actual.Keys) {
    if (-not $allowed.ContainsKey($key)) { $violations.Add("未登记键上下文拼接/切片：$key（actual=$($actual[$key])）") }
    elseif ([int]$actual[$key] -gt [int]$allowed[$key]) {
        $violations.Add("超基线：$key（actual=$($actual[$key]), baseline=$($allowed[$key])）")
    }
}

if ($NegativeControl) {
    $probe = 'var p = ProcessKeys.FamilyPrefix + session;'
    if ($probe -notmatch $rules['concat']) { $violations.Add('负对照失败：拼接样本未命中') }
    $probe2 = 'var q = $"session.{session}.r{round}.";'
    if ($probe2 -notmatch $rules['interp']) { $violations.Add('负对照失败：插值样本未命中') }
    if ('FamilyPrefix.Substring(1)' -notmatch $rules['slice']) { $violations.Add('负对照失败：切片样本未命中') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyOpsClosedSet'
