# Check-KeyParseSingleSource.ps1
# KPK1 解析单源（阻断）：域键类内禁模板匹配/切片/数值解析 —— 解析只能经 KeyOps.TryParse*。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$scanRoots = @(
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain'),
    (Join-Path $Root 'Frame\CsharpPrj\Modules\Node\Identity')
)
$parsePattern = 'IndexOf\(|Substring\(|Split\('
$keysPattern = $parsePattern + '|StartsWith\(|\.TryParse\(|\[\s*\.\.'

function Get-ParseViolations {
    param([string]$File, [string]$Pattern)
    $text = Get-Content -LiteralPath $File -Raw
    # 注释剥离（判据载体 = 代码结构事实，注释提及不算解析实现）。
    $text = [regex]::Replace($text, '/\*.*?\*/', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    $text = [regex]::Replace($text, '//[^\r\n]*', '')
    $hits = [regex]::Matches($text, $Pattern)
    $out = [System.Collections.Generic.List[string]]::new()
    foreach ($hit in $hits) {
        $line = ($text.Substring(0, $hit.Index) -split "`n").Count
        $out.Add(("{0}:{1} 解析原语：{2}" -f (Split-Path -Leaf $File), $line, $hit.Value))
    }

    return $out
}

$violations = [System.Collections.Generic.List[string]]::new()
$scanned = 0
foreach ($scanRoot in $scanRoots) {
    if (-not (Test-Path -LiteralPath $scanRoot)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $scanRoot -File | Where-Object { $_.Name -like '*Keys.cs' -or $_.Name -eq 'NodeEndpointIds.cs' })) {
        if ($file.Name -eq 'DomainKeyOps.cs') { continue }
        $scanned++
        $pattern = if ($file.Name -eq 'NodeEndpointIds.cs') { $parsePattern } else { $keysPattern }
        $found = @(Get-ParseViolations -File $file.FullName -Pattern $pattern)
        if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }
    }
}

if ($scanned -eq 0) { $violations.Add('解析单源扫描面为空（判据不得空转）') }

if ($NegativeControl) {
    $probe = 'var key = raw.Substring(prefix.Length); var at = raw.IndexOf(marker);'
    if ($probe -notmatch $parsePattern) { $violations.Add('负对照失败：切片样本未命中') }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'KeyParseSingleSource'
