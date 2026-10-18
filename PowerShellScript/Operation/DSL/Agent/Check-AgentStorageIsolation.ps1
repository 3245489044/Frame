#requires -Version 5.1
<#
.SYNOPSIS
    AgentStorage 隔离门禁 —— 零落盘 / 零 SQLite / 观测面断言。

.DESCRIPTION
    判据（结构事实，非执行者叙述）：
      A. zeroDisk/strictNoSqlite：worker 根下不得出现 *.db*（sessions/tasks/cron/connectors 全谱）。
      B. strictNoSqlite：探针报告（-ProbeReport）必须证明 node:sqlite 加载 0 次、DatabaseSync 构造 0 次。
      C. 观测面（可选）：-ObserveReport 提供 agent-storage-observe JSON，断言 misses=0 / bindings=[] / fallbacks=0。

.PARAMETER Root
    worker 根（缺省 = Projects\Plug\Agent\data\cline_runtime）。

.PARAMETER Profile
    期望档位（off/relocated/zeroDisk/strictNoSqlite）；缺省读 AGENT_STORAGE_PROFILE，再缺省 zeroDisk。

.PARAMETER ProbeReport
    存储探针 JSON（Invoke-StorageProbe 产物）。

.PARAMETER ObserveReport
    agent-storage-observe 阶段帧 JSON。

.PARAMETER StrictNoSqlite
    启用 B 段硬判（需配合 -ProbeReport）。

.EXAMPLE
    .\Check-AgentStorageIsolation.ps1
    .\Check-AgentStorageIsolation.ps1 -Root Projects\Plug\Agent\data\cline_runtime -StrictNoSqlite -ProbeReport probe.json
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [ValidateSet('off', 'relocated', 'zeroDisk', 'strictNoSqlite')]
    [string]$Profile = '',
    [string]$ProbeReport = '',
    [string]$ObserveReport = '',
    [switch]$StrictNoSqlite,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Join-Path $repoRoot 'Projects\Plug\Agent\data\cline_runtime'
} elseif (-not [System.IO.Path]::IsPathRooted($Root)) {
    $Root = Join-Path $repoRoot $Root
}

if ([string]::IsNullOrWhiteSpace($Profile)) {
    $Profile = if (-not [string]::IsNullOrWhiteSpace($env:AGENT_STORAGE_PROFILE)) {
        $env:AGENT_STORAGE_PROFILE.Trim()
    } else { 'zeroDisk' }
}

$violations = [System.Collections.Generic.List[string]]::new()
$zeroDisk = $Profile -in @('zeroDisk', 'strictNoSqlite')
$strict = $StrictNoSqlite -or $Profile -eq 'strictNoSqlite'

# ── A. 零落盘：worker 根下不得有 *.db* ──
$dbResidue = @()
if (Test-Path -LiteralPath $Root) {
    $dbResidue = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.db*' -ErrorAction SilentlyContinue)
}
if ($zeroDisk) {
    foreach ($f in $dbResidue) {
        $violations.Add("zeroDisk 下仍有 DB 落盘: $($f.FullName)")
    }
}
if ($ShowDetail) {
    Write-Host ("A: Root={0} | 残留 {1} 处 | Profile={2}" -f $Root, $dbResidue.Count, $Profile)
}

# ── B. 严格零 SQLite：探针报告 ──
if ($strict) {
    if ([string]::IsNullOrWhiteSpace($ProbeReport)) {
        $violations.Add('strictNoSqlite 需提供 -ProbeReport（证明 node:sqlite 未加载）')
    } elseif (-not (Test-Path -LiteralPath $ProbeReport)) {
        $violations.Add("探针报告不存在: $ProbeReport")
    } else {
        $probe = Get-Content -LiteralPath $ProbeReport -Raw | ConvertFrom-Json
        $loaded = @($probe.sqliteModulesLoaded) + @($probe.databaseSyncConstructions)
        if ($null -ne $probe.nodeSqliteLoaded -and [int]$probe.nodeSqliteLoaded -gt 0) {
            $violations.Add("strictNoSqlite 违约：node:sqlite 加载 $($probe.nodeSqliteLoaded) 次")
        }
        if ($null -ne $probe.databaseSyncConstructions -and [int]$probe.databaseSyncConstructions -gt 0) {
            $violations.Add("strictNoSqlite 违约：DatabaseSync 构造 $($probe.databaseSyncConstructions) 次")
        }
        if ($loaded.Count -eq 0 -and $null -eq $probe.nodeSqliteLoaded) {
            $violations.Add('探针报告缺少 nodeSqliteLoaded/databaseSyncConstructions 字段')
        }
    }
    if ($ShowDetail) { Write-Host ('B: 严格零 SQLite 已检（报告={0}）' -f $ProbeReport) }
}

# ── C. 观测面：misses=0 / bindings=[] / fallbacks=0 ──
if (-not [string]::IsNullOrWhiteSpace($ObserveReport)) {
    if (-not (Test-Path -LiteralPath $ObserveReport)) {
        $violations.Add("观测报告不存在: $ObserveReport")
    } else {
        $obs = Get-Content -LiteralPath $ObserveReport -Raw | ConvertFrom-Json
        $misses = 0
        if ($null -ne $obs.fs.perSdk) {
            foreach ($p in $obs.fs.perSdk.PSObject.Properties) {
                $misses += [int]$p.Value.misses
                $misses += [int]$p.Value.dbMisses
            }
        }
        if ($misses -gt 0) { $violations.Add("观测面违约：fs/db misses=$misses（应为 0）") }
        if ($null -ne $obs.fs.bindings -and @($obs.fs.bindings).Count -gt 0) {
            $violations.Add("观测面违约：bindings 非空（$(@($obs.fs.bindings) -join ', ')）")
        }
        if ($null -ne $obs.db.fallbacks -and [int]$obs.db.fallbacks -gt 0) {
            $violations.Add("观测面违约：db.fallbacks=$($obs.db.fallbacks)（应为 0）")
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$obs.db.installError)) {
            $violations.Add("观测面违约：db.installError=$($obs.db.installError)")
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host ("Check-AgentStorageIsolation: OK —— Profile={0} | 零落盘/严格面已核。" -f $Profile) -ForegroundColor Green
    exit 0
}

Write-Host "Check-AgentStorageIsolation: FAIL —— 发现 $($violations.Count) 处违规：" -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
exit 1
