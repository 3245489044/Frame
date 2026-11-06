# Check-KeyDeclSingleSource.ps1
# KB2 键形字面量单源（棘轮）：域前缀/模板字面量只允许出现在目录/模板常量/声明装载器/测试。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    # 机器可读出口（P2）：-Json 整块结果；-EmitActual 只输出 actual 表。
    [switch]$Json,
    [switch]$EmitActual
)

$ErrorActionPreference = 'Stop'
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name KeyDeclSingleSource
$whitelist = @($baseline.Whitelist.Value)
$pattern = '"(?:smsg|seg|sv|obs|arch|proc|cline\.session)\.|"\{container\}\.r\{round\}|"session\.\{container\}'

$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
$exclude = 'Modules[\\/]KeySpace[\\/]|Modules[\\/]Parsing[\\/]Keys[\\/]|[\\/](?:obj|bin|BuildOutput)[\\/]|[\\/](?:Tests?|\.Tests)[\\/]'
$actual = @{}
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-GateFiles -Root $scanRoot -Extension cs)) {
        $rel = $file.Substring($Root.Length + 1).Replace('\', '/')
        if ($rel -match $exclude) { continue }
        if ($whitelist -contains $rel) { continue }
        $text = Get-Content -LiteralPath $file -Raw
        if ($null -eq $text) { continue }
        $text = [regex]::Replace($text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
        $text = [regex]::Replace($text, '//[^\r\n]*', '')
        $count = ([regex]::Matches($text, $pattern)).Count
        if ($count -gt 0) { $actual[$rel] = $count }
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
    if (-not $allowed.ContainsKey($key)) { $violations.Add("白名单外键形字面量：$key（actual=$($actual[$key])）") }
    elseif ([int]$actual[$key] -gt [int]$allowed[$key]) {
        $violations.Add("超基线：$key（actual=$($actual[$key]), baseline=$($allowed[$key])）")
    }
}

# actual 三态表（P2）：已登记 / 未登记；未命中项也列出（便于机械对账）。
$actualOut = [ordered]@{}
foreach ($key in $actual.Keys) {
    $state = if ($allowed.ContainsKey($key)) { 'registered' } else { 'unregistered' }
    $limit = if ($allowed.ContainsKey($key)) { [int]$allowed[$key] } else { 0 }
    $actualOut[$key] = [ordered]@{ count = [int]$actual[$key]; state = $state; limit = $limit }
}
foreach ($key in $allowed.Keys) {
    if (-not $actualOut.Contains($key)) { $actualOut[$key] = [ordered]@{ count = 0; state = 'registered'; limit = [int]$allowed[$key] } }
}

if ($NegativeControl) {
    if ('const string P = "proc.";' -notmatch $pattern) { $violations.Add('负对照失败：字面量样本未命中') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyDeclSingleSource' -Json:$Json -EmitActual:$EmitActual -Actual $actualOut
