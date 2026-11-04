#requires -Version 5.1
<#
.SYNOPSIS
    存量边盖章回填（C8）：默认 DryRun 出计数，-Apply 受控写（先备份 graph.db）。

.DESCRIPTION
    判据载体 = 回填报告计数（结构事实；禁执行者叙述）：total / missing / patched / skipped。
    只补字段（assertion / createdSeq / originFlow / originStep），不改边键与端点。
    值来源 = 显式参数（fallbackFlow / fallbackStep），禁按时间猜。
    LiteDB 单 owner（宿主运行时打不开）⇒ 退出码 3 + 关闭宿主指引；禁绕过单写通道。
    报告（-OutFile）供门禁 Check-EdgeStamp 复算：schema=edge-stamp-report/v1。
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [string]$FallbackFlow = 'observed',
    [string]$FallbackStep = '',
    [switch]$Apply,
    [switch]$Json,
    [string]$OutFile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[EdgeStampRepair] $Message") }

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
    schema       = 'edge-stamp-report/v1'
    generatedAt  = (Get-Date).ToUniversalTime().ToString('o')
    graph        = $graphDb
    apply        = [bool]$Apply
    fallbackFlow = $FallbackFlow
    total        = 0
    missing      = 0
    patched      = 0
    skipped      = 0
    failed       = @()
    samples      = @()
    backup       = $null
}

function Write-Report([string]$Path) {
    $json = ConvertTo-Json -InputObject $report -Depth 6
    if ($Json) { $json }
    if ($Path) {
        $dir = Split-Path -Parent $Path
        if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
        Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
    }
}

if (-not (Test-Path -LiteralPath $graphDb)) {
    # 库不存在 = 空图（合法零值）：写出报告（missing=0），供门禁复算。
    Write-Report -Path $OutFile
    if (-not $Json) { Write-Host '[EdgeStampRepair] 图库不存在（按空图处理，missing=0）' -ForegroundColor Yellow }
    exit 0
}

$db = $null
try { $db = New-Object LiteDB.LiteDatabase("Filename=$graphDb") }
catch {
    Write-Fail "图库被独占（LiteDB 单 owner）：请先关闭宿主进程后重跑。原始错误：$($_.Exception.Message)"
    exit 3
}

try {
    $col = $db.GetCollection('ur_references')
    $plan = @()
    foreach ($d in $col.FindAll()) {
        $report.total++
        if (-not $d.ContainsKey('Doc') -or -not $d['Doc'].IsString) { $report.skipped++; continue }

        try { $doc = $d['Doc'].AsString | ConvertFrom-Json }
        catch { $report.skipped++; continue }
        if ($null -eq $doc.__body) { $report.skipped++; continue }

        $body = $doc.__body
        $flow = if ($body.PSObject.Properties.Name -contains 'originFlow') { [string]$body.originFlow } else { '' }
        if (-not [string]::IsNullOrWhiteSpace($flow)) { continue }

        $report.missing++
        $plan += [pscustomobject]@{ Id = $d['_id'].AsString; Doc = $doc; RefType = [string]$body.refType }
        if ($report.samples.Count -lt 5) {
            $report.samples += [pscustomobject]@{ id = $d['_id'].AsString; refType = [string]$body.refType }
        }
    }

    if ($Apply -and $plan.Count -gt 0) {
        $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
        $report.backup = "$graphDb.bak-$stamp"
        Copy-Item -LiteralPath $graphDb -Destination $report.backup -Force

        $seq = [long]([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
        foreach ($item in $plan) {
            try {
                $body = $item.Doc.__body
                $seq++
                $step = if ([string]::IsNullOrWhiteSpace($FallbackStep)) { $item.RefType } else { $FallbackStep }

                $body | Add-Member -NotePropertyName assertion -NotePropertyValue 'observed' -Force
                $body | Add-Member -NotePropertyName createdSeq -NotePropertyValue $seq -Force
                $body | Add-Member -NotePropertyName originFlow -NotePropertyValue $FallbackFlow -Force
                $body | Add-Member -NotePropertyName originStep -NotePropertyValue $step -Force

                $updated = $col.FindById($item.Id)
                if ($null -eq $updated) { continue }
                $updated['Doc'] = ($item.Doc | ConvertTo-Json -Depth 20 -Compress)
                $col.Update($updated)
                $report.patched++
            } catch {
                $report.failed += ("{0}: {1}" -f $item.Id, $_.Exception.Message)
            }
        }
    }
} catch {
    Write-Fail "回填失败：$($_.Exception.Message)"
    $db.Dispose()
    exit 1
}
$db.Dispose()

Write-Report -Path $OutFile
if (-not $Json) {
    Write-Host ''
    Write-Host '== 存量边盖章回填 ==' -ForegroundColor Cyan
    Write-Host ("total={0} missing={1} patched={2} skipped={3}" -f $report.total, $report.missing, $report.patched, $report.skipped)
    if ($report.backup) { Write-Host ("备份：{0}" -f $report.backup) }
    foreach ($f in $report.failed) { Write-Host ("  [FAIL] $f") -ForegroundColor Yellow }
    if (-not $Apply) { Write-Host '（DryRun：加 -Apply 执行；执行前自动备份 graph.db）' }
}

if ($report.failed.Count -gt 0) { exit 1 }
if ($Apply -and $report.patched -lt $report.missing) { exit 1 }
exit 0
