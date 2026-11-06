# Check-KeyParseAmbiguity.ps1
# KPK1 歧义阻断在册（阻断）：解析内核的歧义/作用域/轴约束用例必须在册（行为由 KeySpace.Tests 承担）。
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
$testFile = Join-Path $Root 'Environment\CentralTest\CSharpTest\Modules.Tests\KeySpace.Tests\KeyParseKernelTests.cs'
$required = @(
    'SpecificTemplate_WinsOverBroadTemplate',
    'EqualSpecificity_IsAmbiguousFailure',
    'AxisVocabulary_RejectsUnknownValue',
    'RequiredAxis_MissingIsExplicit',
    'ScopedParse_UsesKnownAxes_AndRejectsForeignScope',
    'TrailingDot_IsPrefixFragment'
)

function Get-AmbiguityViolations {
    param([string]$Text, [string[]]$Cases)
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($case in $Cases) {
        if ($Text -notmatch [regex]::Escape($case)) { $out.Add("歧义/轴/作用域用例未在册：$case") }
    }

    if ($Text -notmatch 'key\.parse-ambiguous') { $out.Add('未断言 key.parse-ambiguous（歧义必须失败）') }
    if ($Text -notmatch 'key\.scope-mismatch') { $out.Add('未断言 key.scope-mismatch（作用域解析）') }
    if ($Text -notmatch 'key\.axis-invalid') { $out.Add('未断言 key.axis-invalid（轴词表）') }

    return $out
}

$violations = [System.Collections.Generic.List[string]]::new()
if (-not (Test-Path -LiteralPath $testFile)) {
    $violations.Add("解析内核用例缺失：$testFile")
} else {
    $found = @(Get-AmbiguityViolations -Text (Get-Content -LiteralPath $testFile -Raw) -Cases $required)
    if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }
}

if ($NegativeControl) {
    $probe = Get-AmbiguityViolations -Text 'class Empty { }' -Cases $required
    if (@($probe).Count -eq 0) { $violations.Add('负对照失败：空用例集未被判红') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyParseAmbiguity'
