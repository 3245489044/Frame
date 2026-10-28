#requires -Version 5.1
<#
.SYNOPSIS
    归档历史损坏图库（graph-log.db → Data\_deprecated\graph-log.db.corrupt-<ts>；只改名不删除）。

.DESCRIPTION
    判据载体 = 文件事实（可打开性 + 是否当前活动库），非叙述：
      · 目标不存在 ⇒ 幂等返回（0 写）
      · 目标可被 LiteDB 只读打开 ⇒ **不归档**（可读库不当作损坏，避免误伤）
      · 目标打不开（损坏）⇒ 默认只输出计划，-Apply 才改名到 <状态根>\_deprecated\
      · 活动图库（graph.db）永不作为归档目标
    纪律：默认 DryRun（0 写）；-Apply 为受控写（只改名，不删除、不改内容）。

.EXAMPLE
    .\Invoke-GraphArchiveLegacyDb.ps1 -Json

.EXAMPLE
    .\Invoke-GraphArchiveLegacyDb.ps1 -Apply
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [switch]$Apply,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[GraphArchive] $Message") }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:WF_DATA_ROOT)) { $StateRoot = $env:WF_DATA_ROOT }
    elseif (-not [string]::IsNullOrWhiteSpace($env:FILEDRIVEN_DATA_ROOT)) { $StateRoot = $env:FILEDRIVEN_DATA_ROOT }
    elseif (-not [string]::IsNullOrWhiteSpace($env:ROUTE_STATE_ROOT)) { $StateRoot = $env:ROUTE_STATE_ROOT }
    else {
        $runtimeJson = Join-Path $repoRoot 'runtime.json'
        if (Test-Path -LiteralPath $runtimeJson) {
            try {
                $declared = [string](Get-Content -LiteralPath $runtimeJson -Raw -Encoding UTF8 | ConvertFrom-Json).data
                if (-not [string]::IsNullOrWhiteSpace($declared)) {
                    $StateRoot = if ([IO.Path]::IsPathRooted($declared)) { $declared } else { Join-Path $repoRoot $declared }
                }
            } catch { }
        }
        if ([string]::IsNullOrWhiteSpace($StateRoot)) { $StateRoot = Join-Path $repoRoot 'Data' }
    }
}
if (-not (Test-Path -LiteralPath $StateRoot)) { Write-Fail "状态根不存在：$StateRoot"; exit 1 }
$StateRoot = (Resolve-Path -LiteralPath $StateRoot).Path

$report = [ordered]@{
    schema = 'graph-archive-v1'
    stateRoot = $StateRoot
    source = (Join-Path $StateRoot 'graph-log.db')
    activeGraph = (Join-Path $StateRoot 'graph.db')
    exists = $false
    readable = $null
    openError = $null
    planned = $null
    archived = $false
    applied = [bool]$Apply
}

if (-not (Test-Path -LiteralPath $report.source)) {
    $report.reason = 'not-found'
    if ($Json) { ConvertTo-Json -InputObject $report -Depth 5 } else { Write-Host "无需归档：$($report.source) 不存在（幂等）" }
    exit 0
}
$report.exists = $true

# 依赖定位（与 GraphDoctor 同源）
if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = @(
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Environment\CentralTest\BuildOutput\Debug\net10.0-windows'),
        (Join-Path $repoRoot 'BuildOutput\publish-smoke-host')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LiteDB.dll') } | Select-Object -First 1
}
$liteDll = if ([string]::IsNullOrWhiteSpace($DllDir)) { '' } else { Join-Path $DllDir 'LiteDB.dll' }
if ([string]::IsNullOrWhiteSpace($liteDll) -or -not (Test-Path -LiteralPath $liteDll)) {
    Write-Fail '缺依赖 LiteDB.dll（-DllDir 指定构建输出；退出码 2）'
    exit 2
}
try { Add-Type -Path $liteDll -ErrorAction Stop } catch { if ($_.Exception.Message -notmatch 'already') { throw } }

try {
    $probe = New-Object LiteDB.LiteDatabase("Filename=$($report.source);ReadOnly=true")
    $probe.Dispose()
    $report.readable = $true
    $report.reason = 'readable'
    if ($Json) { ConvertTo-Json -InputObject $report -Depth 5 } else { Write-Host "不归档：$($report.source) 可正常打开（非损坏）" }
    exit 0
} catch {
    $report.readable = $false
    $report.openError = $_.Exception.Message
}

$deprecatedDir = Join-Path $StateRoot '_deprecated'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$report.planned = (Join-Path $deprecatedDir "graph-log.db.corrupt-$stamp")

if (-not $Apply) {
    $report.reason = 'dry-run'
    if ($Json) { ConvertTo-Json -InputObject $report -Depth 5 }
    else { Write-Host "计划归档（未执行）：$($report.source) → $($report.planned)" -ForegroundColor Yellow; Write-Host '（加 -Apply 执行；仅改名，不删除）' }
    exit 0
}

New-Item -ItemType Directory -Path $deprecatedDir -Force | Out-Null
Move-Item -LiteralPath $report.source -Destination $report.planned -Force
$report.archived = $true
$report.reason = 'archived'
if ($Json) { ConvertTo-Json -InputObject $report -Depth 5 } else { Write-Host "已归档：$($report.source) → $($report.planned)" -ForegroundColor Green }
exit 0
