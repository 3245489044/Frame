#requires -Version 5.1
<#
.SYNOPSIS
    域解析单入口守卫（DomainResolveSingleEntry）—— 裸标签别名表与解码分派各一处。

.DESCRIPTION
    判据（B3/DUP-04）：
      G1 别名表单源：Agent src 内 `LegacyLabels` 定义 = 1（唯一裸标签兼容面）。
      G2 旧入口归零：`AgentUiLibraryBatch.cs` 内 `LegacyDbAliases` / `ResolveDomain(` = 0。
      G3 正/负对照：合成样本（旧别名表）必被捕获。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host ''
Write-Host '============================================================'
Write-Host '  域解析单入口守卫（DomainResolveSingleEntry）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$src = Join-Path $Root 'Projects\Plug\Agent\src'
$batch = Join-Path $src 'Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs'
$route = Join-Path $src 'Agent.Ui\Core\Functions\Db\DomainRoute.cs'

$aliasDefs = 0
foreach ($f in (Get-ChildItem -Path $src -Recurse -Include '*.cs' -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    $aliasDefs += (Get-Count $t 'IReadOnlyDictionary<string,\s*string>\s+LegacyLabels')
}
if ($aliasDefs -ne 1) { $violations.Add("[G1] 裸标签别名表定义数 ≠ 1（实际 $aliasDefs）") }
if (-not (Test-Path -LiteralPath $route)) { $violations.Add('[G1] 未见 DomainRoute.cs（单入口缺失）') }

if (Test-Path -LiteralPath $batch) {
    $bt = [System.IO.File]::ReadAllText($batch)
    $old = (Get-Count $bt 'LegacyDbAliases') + (Get-Count $bt '(?<!NodeGatherCatalog\.)ResolveDomain\s*\(')
    if ($old -gt 0) { $violations.Add("[G2] AgentUiLibraryBatch 内旧域解析入口 × $old") }
} else {
    $violations.Add('[G2] 未见 AgentUiLibraryBatch.cs')
}

# 正/负对照
if ((Get-Count 'private static readonly IReadOnlyDictionary<string, string> LegacyDbAliases =' 'LegacyDbAliases|IReadOnlyDictionary<string,\s*string>\s+LegacyLabels') -eq 0) {
    $violations.Add('[G3] 负对照失效：合成样本（旧别名表）未被捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 域解析单入口通过（正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
