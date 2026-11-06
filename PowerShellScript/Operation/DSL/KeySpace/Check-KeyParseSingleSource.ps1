# Check-KeyParseSingleSource.ps1
# KPK1 解析单源（阻断）：域键类内禁模板匹配/切片/数值解析 —— 解析只能经 KeyOps.TryParse*。
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

$scanRoots = @(
    @{ Path = (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain'); KeysOnly = $true },
    @{ Path = (Join-Path $Root 'Frame\CsharpPrj\Modules\Data\Node\Identity'); KeysOnly = $true },
    # 读侧/装配侧容器解析面（KPK1 扩展）：类型化后这些目录不得再出现解析原语。
    @{ Path = (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\NodeSpec'); KeysOnly = $false },
    @{ Path = (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Gather'); KeysOnly = $false }
)
# 显式豁免位：解析桥（唯一允许持有 resolver 入口的实现）与解析器单源。
$allowlisted = @('DomainKeyOps.cs', 'NodeContainerCoordinates.cs')
$parsePattern = 'IndexOf\(|Substring\(|Split\('
# KeyOps 是唯一解析入口：`Ops.TryParse(...)` 属允许面（判据只禁 .NET/Enum 等原生解析原语）。
$keysPattern = $parsePattern + '|StartsWith\(|(?<!Ops)\.TryParse\(|\[\s*\.\.'

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
    $rootPath = [string]$scanRoot.Path
    if (-not (Test-Path -LiteralPath $rootPath)) { continue }
    $files = Get-ChildItem -LiteralPath $rootPath -File -Recurse | Where-Object {
        if ($allowlisted -contains $_.Name) { return $false }
        if ($scanRoot.KeysOnly) { return $_.Name -like '*Keys.cs' -or $_.Name -eq 'NodeEndpointIds.cs' }
        if ($_.Extension -ne '.cs') { return $false }
        # 非域类目录：只扫**容器坐标装配面**（结构事实 = 引用读请求/坐标类型），
        # 避免把参数解析/标签切分等既存原语（JsonParamReader / NodeTagWriter 等）混入判据。
        $text = Get-Content -LiteralPath $_.FullName -Raw
        return $text -match 'NodeReadRequest|ContainerCoordinate'
    }
    foreach ($file in @($files)) {
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
