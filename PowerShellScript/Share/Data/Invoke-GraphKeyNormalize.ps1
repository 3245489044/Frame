#requires -Version 5.1
<#
.SYNOPSIS
    图边遗留键规范化（U+0001 → "::"）：DryRun 出计划，-Apply 受控写（先备份）。

.DESCRIPTION
    判据载体 = 键形态（含 U+0001 即遗留）+ 冲突计数，非叙述。默认 DryRun（0 写）。
    -Apply：先复制 graph.db → graph.db.bak-<ts>，再逐条重写 _id（同三元组已存在 ⇒ 仅删遗留，去重）；写后复算 legacy=0。
    LiteDB 单 owner（宿主运行时打不开）⇒ 退出码 3 + 关闭宿主指引；禁绕过单写通道。

.EXAMPLE
    .\Invoke-GraphKeyNormalize.ps1 -Json

.EXAMPLE
    .\Invoke-GraphKeyNormalize.ps1 -Apply
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

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[GraphKeyNormalize] $Message") }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:WF_DATA_ROOT)) { $StateRoot = $env:WF_DATA_ROOT }
    elseif (-not [string]::IsNullOrWhiteSpace($env:FILEDRIVEN_DATA_ROOT)) { $StateRoot = $env:FILEDRIVEN_DATA_ROOT }
    elseif (-not [string]::IsNullOrWhiteSpace($env:ROUTE_STATE_ROOT)) { $StateRoot = $env:ROUTE_STATE_ROOT }
    else {
        if (Test-Path -LiteralPath (Join-Path $repoRoot 'runtime.json')) {
            try {
                $declared = [string](Get-Content -LiteralPath (Join-Path $repoRoot 'runtime.json') -Raw -Encoding UTF8 | ConvertFrom-Json).data
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
$graphDb = Join-Path $StateRoot 'graph.db'
if (-not (Test-Path -LiteralPath $graphDb)) { Write-Fail "图库不存在：$graphDb（退出码 1）"; exit 1 }

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

$report = [ordered]@{
    schema = 'graph-key-normalize-v1'
    graph = $graphDb
    applied = [bool]$Apply
    legacy = 0
    duplicates = 0
    rewritten = 0
    legacyAfter = $null
    failed = @()
    samples = @()
    backup = $null
}

$db = $null
try { $db = New-Object LiteDB.LiteDatabase("Filename=$graphDb") }
catch {
    Write-Fail "图库被独占（LiteDB 单 owner）：请先关闭宿主进程后重跑。原始错误：$($_.Exception.Message)"
    exit 3
}

# 遗留键切分（U+0001 恰两段分隔；与 EdgeIdKeys.TryParse 的 Legacy surface 同构）
function Split-LegacyId {
    param([string]$Id)
    $parts = $Id.Split([char]1)
    if ($parts.Length -ne 3) { return $null }
    if ([string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1]) -or [string]::IsNullOrWhiteSpace($parts[2])) { return $null }
    return $parts
}

function Get-IdOf {
    param($Doc)
    if ($Doc.ContainsKey('_id') -and $Doc['_id'].IsString) { return $Doc['_id'].AsString }
    return ''
}

try {
    $col = $db.GetCollection('ur_references')
    $plan = @()
    foreach ($d in $col.FindAll()) {
        $id = Get-IdOf -Doc $d
        if ($id.IndexOf([char]1) -lt 0) { continue }
        $report.legacy++
        $parts = Split-LegacyId -Id $id
        if ($null -eq $parts) {
            $report.failed += "无法解析的遗留键：$($id -replace [string][char]1, '<U+0001>')"
            continue
        }
        $canonical = "$($parts[0])::$($parts[1])::$($parts[2])"
        $exists = $null -ne $col.FindById($canonical)
        if ($exists) { $report.duplicates++ }
        $plan += [pscustomobject]@{ Legacy = $id; Canonical = $canonical; Duplicate = $exists }
        if ($report.samples.Count -lt 5) {
            $report.samples += [pscustomobject]@{
                legacy = ($id -replace [string][char]1, '<U+0001>'); canonical = $canonical; duplicate = $exists
            }
        }
    }

    if ($Apply -and $plan.Count -gt 0) {
        $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
        $report.backup = "$graphDb.bak-$stamp"
        Copy-Item -LiteralPath $graphDb -Destination $report.backup -Force

        foreach ($item in $plan) {
            try {
                if (-not $item.Duplicate) {
                    $src = $col.FindById($item.Legacy)
                    if ($null -ne $src) {
                        $copy = New-Object LiteDB.BsonDocument
                        $copy['_id'] = $item.Canonical
                        foreach ($field in @('Doc', 'SourceId', 'TargetId', 'RefType')) {
                            if ($src.ContainsKey($field)) { $copy[$field] = $src[$field] }
                        }
                        $col.Insert($copy)
                    }
                }
                $col.Delete($item.Legacy)
                $report.rewritten++
            } catch {
                $report.failed += ("{0} → {1}: {2}" -f ($item.Legacy -replace [string][char]1, '<U+0001>'), $item.Canonical, $_.Exception.Message)
            }
        }

        $remaining = 0
        foreach ($d in $col.FindAll()) {
            if ((Get-IdOf -Doc $d).IndexOf([char]1) -ge 0) { $remaining++ }
        }
        $report.legacyAfter = $remaining
    }
} catch {
    Write-Fail "规范化失败：$($_.Exception.Message)"
    $db.Dispose()
    exit 1
}
$db.Dispose()

$reportJson = ConvertTo-Json -InputObject $report -Depth 6
if ($Json) { $reportJson } else {
    Write-Host ''
    Write-Host '== 图边遗留键规范化 ==' -ForegroundColor Cyan
    Write-Host ("遗留键={0}  重复={1}  已重写={2}" -f $report.legacy, $report.duplicates, $report.rewritten)
    foreach ($s in $report.samples) { Write-Host ("  {0} → {1}（重复={2}）" -f $s.legacy, $s.canonical, $s.duplicate) }
    if ($report.backup) { Write-Host ("备份：{0}" -f $report.backup) }
    if ($null -ne $report.legacyAfter) { Write-Host ("写后遗留={0}" -f $report.legacyAfter) }
    foreach ($f in $report.failed) { Write-Host ("  [FAIL] $f") -ForegroundColor Yellow }
    if (-not $Apply) { Write-Host '（DryRun：加 -Apply 执行；执行前自动备份 graph.db）' }
}

if ($report.failed.Count -gt 0) { exit 1 }
if ($Apply -and $null -ne $report.legacyAfter -and $report.legacyAfter -gt 0) { exit 1 }
exit 0
