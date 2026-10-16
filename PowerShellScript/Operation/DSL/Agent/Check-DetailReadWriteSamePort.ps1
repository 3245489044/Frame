#requires -Version 5.1
<#
.SYNOPSIS
    详情读=写同端口守卫（DetailReadWriteSamePort）—— G3 行身份聚拢点。

.DESCRIPTION
    判据（`AgentUiLibraryBatch.LoadNodeDetailAsync` 方法体）：
      G1 读经**全域行端口**：体内含 `AgentWriteSources.TryGetRecord`。
      G2 域单点查询 = 0：体内 `TryGetNodeAsync` 命中 = 0（读身份 ≠ 写身份即红）。
      G3 正/负对照：合成样本（体内含 TryGetNodeAsync）必被 G2 捕获（防判据空转）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  详情读=写同端口守卫（DetailReadWriteSamePort：G3）'
Write-Host '============================================================'

$batchFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs'

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
        if ((Get-PortMisses 'var record = await AgentUiLibraryData.TryGetNodeAsync(key)').Count -eq 0) {
            $violations.Add('[G3] 负对照失效：域单点查询的合成样本未被判据捕获')
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3 通过（详情读经全域行端口 + 零域单点查询）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
