#requires -Version 5.1
<#
.SYNOPSIS
    详情读=写同端口守卫（DetailReadWriteSamePort）—— G3 行身份聚拢点。

.DESCRIPTION
    判据（`AgentUiLibraryBatch.LoadNodeDetailAsync` 方法体）：
      G1 读经**全域行端口**：体内含 `AgentWriteSources.TryGetRecord`。
      G2 域单点查询 = 0：体内 `TryGetNodeAsync` 命中 = 0（读身份 ≠ 写身份即红）。
      G3 正/负对照：合成样本（体内含 TryGetNodeAsync）必被 G2 捕获（防判据空转）。
    正/负对照由 Assert-GateControls 统一执行。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'DetailReadWriteSamePort'
$violations = $ctx.Violations

Write-GateHeader '详情读=写同端口守卫（DetailReadWriteSamePort：G3）'

# 判据自证（与 G1/G2 共用同一谓词）
$controls = @(
    @{ Id = 'G1-port'; Predicate = { param($t) $t -notmatch 'AgentWriteSources\.TryGetRecord' }
       Good = 'var record = await AgentWriteSources.TryGetRecord(key);'; Bad = 'var record = await AgentUiLibraryData.TryGetNodeAsync(key)' }
    @{ Id = 'G2-domain'; Predicate = { param($t) $t -match 'TryGetNodeAsync' }
       Good = 'var record = await AgentWriteSources.TryGetRecord(key);'; Bad = 'var record = await AgentUiLibraryData.TryGetNodeAsync(key)' }
)
foreach ($issue in (Assert-GateControls -Cases $controls -Label 'DetailReadWriteSamePort')) { $violations.Add($issue) }

$batchFile = Join-Path (Get-MappedPath -Key 'AgentSrc') 'Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs'

function Get-DetailLoadBody([string]$Text) {
    $sig = 'private static Task LoadNodeDetailAsync('
    $start = $Text.IndexOf($sig)
    if ($start -lt 0) { return $null }
    $end = $Text.IndexOf('private static void ClearDetail', $start)
    if ($end -lt 0) { $end = [Math]::Min($start + 4000, $Text.Length) }
    return $Text.Substring($start, $end - $start)
}

function Get-PortMisses([string]$Body) {
    $misses = [System.Collections.Generic.List[string]]::new()
    if ($Body -notmatch 'AgentWriteSources\.TryGetRecord') { $misses.Add('详情读未走全域行端口（TryGetRecord）') }
    if ($Body -match 'TryGetNodeAsync') { $misses.Add('详情读残留域单点查询（TryGetNodeAsync）：读身份 ≠ 写身份') }
    return $misses
}

if (-not (Test-Path -LiteralPath $batchFile)) {
    $violations.Add("[G1] 缺件（判据不可判）：$batchFile")
} else {
    $body = Get-DetailLoadBody ([System.IO.File]::ReadAllText($batchFile))
    if ($null -eq $body) {
        $violations.Add('[G1] 未定位 LoadNodeDetailAsync（判据不可判）')
    } else {
        foreach ($m in (Get-PortMisses $body)) { $violations.Add("[G1] $m") }
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'DetailReadWriteSamePort'
