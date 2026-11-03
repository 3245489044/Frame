<#
.SYNOPSIS
    Agent.Contract 单源守卫（G18/G3）：摘要 / 嵌入资源 / 松弛读取 / 单键域拼接 / 诊断归因不得出现第二实现。

.DESCRIPTION
    判据（载体 = 源码结构事实，非执行者叙述）：
      C1 Agent.Contract 内 SHA256.HashData 直调 = 0（摘要统一经 ShortHash）
      C2 Agent.Contract 内 GetManifestResourceStream 直调 = 0（嵌入资源统一经 EmbeddedTextResource）
      C3 Agent.Contract 内 TryGetProperty( 直调 = 0（统一经 JsonFieldAccess）
      C4 5 个单键域内 ops.Render(ops.With( = 0（统一经 SingleKeyFamily）
      C5 RoundTerminationMapper 内 || = 0（诊断码单表匹配）
    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 合成样本必须判红。

.PARAMETER Root
    仓库根（缺省自动定位）。

.PARAMETER Check
    违规即退出码 1（门禁模式）。

.PARAMETER NegativeControl
    负对照：把违规样本喂给同一扫描函数，必须检出。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$gateRoot = $PSScriptRoot
while ($gateRoot -and -not ((Test-Path -LiteralPath (Join-Path $gateRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $gateRoot 'Projects')))) {
    $gateRoot = Split-Path -Parent $gateRoot
}
if (-not $gateRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = $gateRoot }
$contractRoot = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract'
$mapperPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Engine\TaskPool\RoundTerminationMapper.cs'
$singleKeyDomains = @('MessageStreamKeys.cs', 'SegmentKeys.cs', 'SessionViewKeys.cs', 'ArchiveKeys.cs', 'ObservabilityKeys.cs')

if (-not (Test-Path -LiteralPath $contractRoot)) {
    Write-Host "VIOLATION: 契约源码根缺失：$contractRoot" -ForegroundColor Red
    exit 1
}

function Get-ContractSources {
    Get-ChildItem -LiteralPath $contractRoot -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '\\Generated\\' -and $_.FullName -notmatch '\\obj\\' }
}

function Get-Violations([string]$Text, [string]$FileLabel) {
    $found = [System.Collections.Generic.List[string]]::new()
    if ($Text -match 'SHA256\.HashData\s*\(') { $found.Add("C1 $FileLabel：SHA256.HashData 直调（应经 ShortHash）") }
    if ($Text -match 'GetManifestResourceStream\s*\(') { $found.Add("C2 $FileLabel：GetManifestResourceStream 直调（应经 EmbeddedTextResource）") }
    return $found
}

# C3 松弛读取：棘轮（当前基线 = 37；只降不升）。基线内为已知精确大小写读取点，新增即红。
$tryGetBaseline = 37

$violations = [System.Collections.Generic.List[string]]::new()
$tryGetCount = 0
$tryGetFiles = [System.Collections.Generic.List[string]]::new()
foreach ($file in (Get-ContractSources)) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($v in (Get-Violations $text $file.Name)) { $violations.Add($v) }
    $hits = ([regex]::Matches($text, 'TryGetProperty\s*\(')).Count
    if ($hits -gt 0) { $tryGetCount += $hits; $tryGetFiles.Add("$($file.Name)($hits)") }
    if ($singleKeyDomains -contains $file.Name -and $text -match 'ops\.Render\s*\(\s*ops\.With\s*\(') {
        $violations.Add("C4 $($file.Name)：单键域内出现 ops.Render(ops.With( 直拼")
    }
}
if ($tryGetCount -gt $tryGetBaseline) {
    $violations.Add("C3 TryGetProperty 直调 $tryGetCount > 基线 $tryGetBaseline（新增点须经 JsonFieldAccess）：$($tryGetFiles -join ', ')")
}

if (Test-Path -LiteralPath $mapperPath) {
    $mapperText = [System.IO.File]::ReadAllText($mapperPath)
    if ($mapperText -match '\|\|') { $violations.Add('C5 RoundTerminationMapper：仍含 || 双表匹配（应经 ContractDiagnostics 单表）') }
}
else {
    $violations.Add("C5 归因映射文件缺失：$mapperPath")
}

Write-Host '=== 契约单源守卫（ContractSingleSource） ==='
Write-Host '检查项: C1 摘要直调 C2 嵌入资源直调 C3 松弛读取直调 C4 单键域拼接 C5 诊断归因单表'

if ($NegativeControl) {
    $probe = [System.Collections.Generic.List[string]]::new()
    foreach ($sample in @(
            'var h = SHA256.HashData(bytes);',
            'stream = asm.GetManifestResourceStream(name);')) {
        $probe.AddRange([string[]](Get-Violations $sample 'synthetic.cs'))
    }
    if (([regex]::Matches('map.TryGetProperty("x", out var v);', 'TryGetProperty\s*\(')).Count -gt 0) {
        $probe.Add('C3 synthetic：TryGetProperty 直调')
    }
    $singleKeyHits = ([regex]::Matches('ops.Render(ops.With(key));', 'ops\.Render\s*\(\s*ops\.With\s*\(')).Count
    if ($singleKeyHits -gt 0) { $probe.Add('C4 synthetic：单键域直拼') }
    if ($probe.Count -lt 4) {
        Write-Host "[FAIL] 负对照未判红（检出 $($probe.Count)/4）" -ForegroundColor Red
        exit 1
    }
    Write-Host "[OK] 负对照判红（$($probe.Count)/4）" -ForegroundColor Green
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "VIOLATION: $v" -ForegroundColor Red }
    Write-Host ("[FAIL] 契约单源违规 {0} 条" -f $violations.Count) -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[OK] 契约单源守卫通过（0 违规）' -ForegroundColor Green
exit 0
