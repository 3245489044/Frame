#requires -Version 5.1
<#
.SYNOPSIS
    行身份单源守卫（ReadIdentitySingleSource）—— 读=写=删=详情同一行身份（结构事实：定义点计数）。

.DESCRIPTION
    基线 = Baselines\Data\ReadIdentitySingleSource.psd1。检查项：
      RI1 SQLite 存储键→表名映射定义点 = 1（DbStoreKeys.SqliteTableFor）
      RI2 旧标签兼容表定义点 = 1（DomainRoute.LegacyLabels）
      RI3 域注册表构造点 ≤ 基线（新增域 = 目录增一行，禁新增注册面）
      RI4 节点域单点读调用点 ≤ 基线（详情/编辑须走行身份端口）

    违例 → 退出码 1（-Report 仅报告）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Report,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name ReadIdentitySingleSource

$agentRoot = Join-Path $Root 'Projects\Plug\Agent\src'
$files = if (Test-Path $agentRoot) {
    Get-ChildItem -Path $agentRoot -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\|\\_deprecated\\' }
} else { @() }

function Count-Hits([string]$pattern) {
    $hits = @($files | Select-String -Pattern $pattern -SimpleMatch -ErrorAction SilentlyContinue)
    if ($ShowDetail) {
        foreach ($h in $hits) {
            Write-Host ("    {0}:{1}  {2}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber, $h.Line.Trim()) -ForegroundColor DarkGray
        }
    }
    return $hits.Count
}

# RI1 / RI2：定义点（含方法/字段声明关键字，避免命中调用点）。
$tableMapDef = Count-Hits 'public static string? SqliteTableFor'
$legacyDef = Count-Hits 'private static readonly IReadOnlyDictionary<string, string> LegacyLabels'
$registryNew = Count-Hits 'new StoreDomainRegistry<'
$nodeReadCalls = (Count-Hits 'AgentUiLibraryData.TryGetNodeAsync') + (Count-Hits 'await TryGetNodeAsync')

$checks = @(
    @{ Id = 'RI1'; Name = '存储键→表名映射定义点'; Actual = $tableMapDef; Expected = [int]$baseline.TableMapDefinition.Value },
    @{ Id = 'RI2'; Name = '旧标签兼容表定义点'; Actual = $legacyDef; Expected = [int]$baseline.LegacyAliasDefinition.Value },
    @{ Id = 'RI3'; Name = '域注册表构造点'; Actual = $registryNew; Expected = [int]$baseline.DomainRegistryConstruction.Value },
    @{ Id = 'RI4'; Name = '节点域单点读调用点'; Actual = $nodeReadCalls; Expected = [int]$baseline.NodeReadCallSites.Value }
)

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  行身份单源守卫（ReadIdentitySingleSource）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($c in $checks) {
    Write-Host ("  {0} {1}：实测 {2} / 期望 {3}" -f $c.Id, $c.Name, $c.Actual, $c.Expected) -ForegroundColor Gray
    if ($c.Actual -ne $c.Expected) { $violations.Add(("{0} {1}：实测 {2}，期望 {3}" -f $c.Id, $c.Name, $c.Actual, $c.Expected)) }
}

if ($violations.Count -eq 0) {
    Write-Host ""
    Write-Host "[OK] ReadIdentitySingleSource：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] ReadIdentitySingleSource 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Report) { exit 0 }
exit 1
