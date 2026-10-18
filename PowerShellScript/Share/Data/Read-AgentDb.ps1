<#
.SYNOPSIS
    Agent 框架数据库只读检视（SQLite agent.db + LiteDB graph.db）—— headless 诊断面导出。
.DESCRIPTION
    补 GUI 面（AgentUi_DbReadTableMarkdown，依赖交互式宿主、不可脚本化/不可 CI 复算）缺失的
    headless 只读导出：单一职责 = 表/集合 → 行原样导出（0 写 / 0 迁移 / 0 裁决）。

    两种库两种驱动：SQLite（System.Data.SQLite）与 LiteDB（LiteDB）。锁语义不同——
    - agent.db（SQLite）：ReadOnly=True 可与运行中的宿主并发读；
    - graph.db（LiteDB）：独占/单 owner（ReferenceGraphStoreRegistry），宿主运行时会打不开，
      须先关闭宿主（本脚本只给可执行指引，不绕过单 owner 纪律）。

    本脚本为诊断面导出，不替代产品读路径（IHistoryStore / IHistoryRelationManager）。

    退出码：0 成功 / 1 库缺失·被占用·用法错 / 2 依赖 DLL 缺失或未知异常。
.EXAMPLE
    .\Read-AgentDb.ps1 -List
.EXAMPLE
    .\Read-AgentDb.ps1 -Store graph -Collection ur_references -Json
.EXAMPLE
    .\Read-AgentDb.ps1 -Store agent -Table ur_tasks -Where "NodeKey LIKE 'vh_%'" -Limit 10
#>
[CmdletBinding()]
param(
    [ValidateSet('agent', 'graph', 'all')][string]$Store = 'all',
    [string]$Table = '',
    [string]$Collection = '',
    [string]$Where = '',
    [int]$Limit = 50,
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [switch]$List,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[Read-AgentDb] $Message") }

# ── 1) 依赖 DLL 定位（构建输出，非 PATH；-DllDir 可覆盖）──
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = @(
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'System.Data.SQLite.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($DllDir) -or -not (Test-Path -LiteralPath $DllDir)) {
    Write-Fail "未找到 System.Data.SQLite.dll；请先构建 FileDriven 或用 -DllDir 指定（缺依赖 ⇒ 退出码 2）。"
    exit 2
}
$DllDir = (Resolve-Path -LiteralPath $DllDir).Path
$sqliteDll = Join-Path $DllDir 'System.Data.SQLite.dll'
$liteDll = Join-Path $DllDir 'LiteDB.dll'

$needSqlite = $Store -in @('agent', 'all')
$needLite = $Store -in @('graph', 'all')
foreach ($req in @(@{ Need = $needSqlite; Dll = $sqliteDll }, @{ Need = $needLite; Dll = $liteDll })) {
    if ($req.Need -and -not (Test-Path -LiteralPath $req.Dll)) {
        Write-Fail "缺依赖：$($req.Dll)（缺依赖 ⇒ 退出码 2）。"
        exit 2
    }
}

function Import-Assembly {
    param([string]$Path)
    try { Add-Type -Path $Path -ErrorAction Stop }
    catch { if ($_.Exception.Message -notmatch 'already') { throw } }
}

# ── 2) 状态根解析（对齐 AgentStateLocator / RouteStateLocation：环境变量 → 仓库级默认）──
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
$agentDb = Join-Path $StateRoot 'agent.db'
$graphDb = Join-Path $StateRoot 'graph.db'

# ── 只读读法（两驱动）──
function Read-Sqlite {
    param([string]$DbPath, [string]$Table, [string]$Where, [int]$Limit, [bool]$ListTables)
    if (-not (Test-Path -LiteralPath $DbPath)) { throw "SQLite 库不存在：$DbPath" }
    Import-Assembly $sqliteDll
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;ReadOnly=True;Version=3;")
    $cn.Open()
    try {
        if ($ListTables) {
            $c = $cn.CreateCommand(); $c.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
            $r = $c.ExecuteReader(); $t = @(); while ($r.Read()) { $t += $r.GetString(0) }; $r.Close()
            return [pscustomobject]@{ Store = 'agent'; Db = $DbPath; Tables = $t }
        }
        # -Where 为原样过滤片段；禁 ';'（防多语句注入）
        if ($Where -and $Where.Contains(';')) { throw "-Where 不得含 ';'（仅允许单一过滤片段）。" }
        $sql = "SELECT * FROM [$Table]"
        if ($Where) { $sql += " WHERE $Where" }
        if ($Limit -gt 0) { $sql += " LIMIT $Limit" }
        $c = $cn.CreateCommand(); $c.CommandText = $sql; $r = $c.ExecuteReader()
        $cols = @(); for ($i = 0; $i -lt $r.FieldCount; $i++) { $cols += $r.GetName($i) }
        $rows = @()
        while ($r.Read()) {
            $o = [ordered]@{}
            for ($i = 0; $i -lt $cols.Count; $i++) {
                $v = $r.GetValue($i); if ($v -is [System.DBNull]) { $v = $null }
                $o[$cols[$i]] = $v
            }
            $rows += [pscustomobject]$o
        }
        $r.Close()
        return [pscustomobject]@{ Store = 'agent'; Db = $DbPath; Table = $Table; Count = $rows.Count; Rows = $rows }
    } finally { $cn.Dispose() }
}

function Read-LiteGraph {
    param([string]$DbPath, [string]$Collection, [int]$Limit, [bool]$ListCollections)
    if (-not (Test-Path -LiteralPath $DbPath)) { throw "LiteDB 图库不存在：$DbPath" }
    Import-Assembly $liteDll
    try { $db = New-Object LiteDB.LiteDatabase("Filename=$DbPath;ReadOnly=true") }
    catch { throw "图库被占用（LiteDB 独占/单 owner）：请先关闭 FileDriven.Launcher 再读。原始错误：$($_.Exception.Message)" }
    try {
        $names = @($db.GetCollectionNames())
        if ($ListCollections) { return [pscustomobject]@{ Store = 'graph'; Db = $DbPath; Collections = $names } }
        $col = $db.GetCollection($Collection)
        $docs = @(); $i = 0
        foreach ($d in $col.FindAll()) {
            if ($Limit -gt 0 -and $i -ge $Limit) { break }
            $docs += $d.ToString(); $i++
        }
        return [pscustomobject]@{ Store = 'graph'; Db = $DbPath; Collection = $Collection; Count = $docs.Count; Docs = $docs }
    } finally { $db.Dispose() }
}

# ── 3) 主流程 ──
if (-not $List -and [string]::IsNullOrWhiteSpace($Table) -and $Store -in @('agent', 'all')) { $Table = 'ur_nodes' }
if (-not $List -and [string]::IsNullOrWhiteSpace($Collection) -and $Store -in @('graph', 'all')) { $Collection = 'ur_references' }

$rc = 1
try {
    $out = @()
    if ($Store -in @('agent', 'all')) { $out += Read-Sqlite -DbPath $agentDb -Table $Table -Where $Where -Limit $Limit -ListTables:$List }
    if ($Store -in @('graph', 'all')) { $out += Read-LiteGraph -DbPath $graphDb -Collection $Collection -Limit $Limit -ListCollections:$List }

    if ($Json) {
        ConvertTo-Json -InputObject @($out) -Depth 6
    } else {
        foreach ($o in $out) {
            Write-Host ('== {0} [{1}]' -f $o.Db, $o.Store)
            if ($o.PSObject.Properties['Tables']) { $o.Tables | ForEach-Object { Write-Host "   table: $_" } }
            if ($o.PSObject.Properties['Collections']) { $o.Collections | ForEach-Object { Write-Host "   collection: $_" } }
            if ($o.PSObject.Properties['Rows']) {
                if ($o.Rows.Count -gt 0) { $o.Rows | Format-Table -AutoSize | Out-String | Write-Host }
                else { Write-Host '   (0 rows)' }
            }
            if ($o.PSObject.Properties['Docs']) {
                if ($o.Docs.Count -gt 0) { $o.Docs | ForEach-Object { Write-Host $_ } }
                else { Write-Host '   (0 docs)' }
            }
        }
    }
    $rc = 0
} catch {
    Write-Fail $_.Exception.Message
    $rc = 1
}
exit $rc
