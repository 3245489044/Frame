#requires -Version 5.1
<#
.SYNOPSIS
    渲染层存储解耦守卫（RenderLayerStoreFree）—— 渲染层只接收模型，禁持有存储端口。

.DESCRIPTION
    基线 = Baselines\Data\RenderLayerStoreFree.psd1。
    RL1 `Agent.Ui\Ui\Views\**` 内 INodeStore | IStoreRowPort | IHistoryStore | INodeDomainPort 引用计数 = 基线（期望 0）。
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
$baseline = Import-GateBaseline -Name RenderLayerStoreFree

$viewsRoot = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views'
$hits = @()
if (Test-Path $viewsRoot) {
    $files = Get-ChildItem -Path $viewsRoot -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\BuildOutput\\' }
    $hits = @($files | Select-String -Pattern 'INodeStore|IStoreRowPort|IHistoryStore|INodeDomainPort' -ErrorAction SilentlyContinue)
}

$actual = $hits.Count
$expected = [int]$baseline.StorePortReferences.Value

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  渲染层存储解耦守卫（RenderLayerStoreFree）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ("  RL1 渲染层存储端口引用：实测 {0} / 期望 {1}" -f $actual, $expected) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($h in $hits) {
        Write-Host ("    {0}:{1}  {2}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber, $h.Line.Trim()) -ForegroundColor DarkGray
    }
}

if ($actual -le $expected) {
    Write-Host ""
    Write-Host "[OK] RenderLayerStoreFree：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host ("[FAIL] RL1 渲染层存储端口引用 {0} > 期望 {1}" -f $actual, $expected) -ForegroundColor Yellow
if ($Report) { exit 0 }
exit 1
