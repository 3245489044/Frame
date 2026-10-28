#requires -Version 5.1
<#
.SYNOPSIS
    关系图重建入口（graph-rebuild）—— M1 计划面（DryRun，0 写）+ Apply 宿主守卫。

.DESCRIPTION
    判据载体 = 结构事实（SQL 行 / 列探测 / 动作计数），禁执行者叙述：
      · DryRun（默认）：只读 SQLite `ur_tasks`，按声明动作投影生成待重建清单（0 写 0 裁决）
      · Apply：本入口**不直接写图**——重建写必须经 C# 内核（Origin=Rebuild）；
        未接宿主 ⇒ fail-fast 退出 2（禁以 PS 直写冒充重建）

    动作投影（与 C# RebuildProjections 同口径）：
      · HistoryId 非空      → mount_task_history（history → task）
      · ParentTaskKey 非空  → mount_subtask（parent → task）
      · RelatedNodes 非空   → mount_node_task（task → nodes，批量）

    列探测：PRAGMA table_info；缺列 ⇒ 显式 skipped 原因（禁以 0 冒充「无数据」）。

.PARAMETER StateRoot
    状态根（缺省按 WF_DATA_ROOT → FILEDRIVEN_DATA_ROOT → ROUTE_STATE_ROOT → runtime.json.data → <repo>\Data）。

.PARAMETER DllDir
    含 System.Data.SQLite.dll 的目录（缺省 BuildOutput\Debug）。

.PARAMETER Apply
    申请写图（须宿主接线；未接线恒退出 2）。

.PARAMETER Json
    结构化 JSON 输出。

.PARAMETER OutFile
    报告落盘路径（诊断产物，只增不改）。
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [switch]$Apply,
    [switch]$Json,
    [string]$OutFile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[GraphRebuild] $Message") }

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
$sqlitePath = Join-Path $StateRoot 'agent.db'
if (-not (Test-Path -LiteralPath $sqlitePath)) { Write-Fail "SQLite 库不存在：$sqlitePath"; exit 1 }

if ($Apply) {
    Write-Fail 'Apply 须经宿主服务：Agent 宿主内 IGraphRebuildService.RunAsync(dryRun:false)（IRelationPorts + Origin=Rebuild）；CLI 不直写图'
    exit 2
}

function Resolve-DllRoot {
    param([string]$Hint)
    if (-not [string]::IsNullOrWhiteSpace($Hint)) {
        if (Test-Path -LiteralPath (Join-Path $Hint 'System.Data.SQLite.dll')) { return (Resolve-Path -LiteralPath $Hint).Path }
    }
    $candidates = @(
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release'),
        (Join-Path $repoRoot 'Environment\CentralTest\BuildOutput\Debug\net10.0-windows'),
        (Join-Path $repoRoot 'BuildOutput\publish-smoke-host'),
        (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Debug')
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $c 'System.Data.SQLite.dll')) { return (Resolve-Path -LiteralPath $c).Path }
    }
    return $null
}

$dllRoot = Resolve-DllRoot -Hint $DllDir
if ($null -eq $dllRoot) { Write-Fail 'System.Data.SQLite.dll 缺失（先构建宿主或指定 -DllDir）'; exit 2 }
$sqliteDll = Join-Path $dllRoot 'System.Data.SQLite.dll'

try { Add-Type -Path $sqliteDll -ErrorAction Stop }
catch { if ($_.Exception.Message -notmatch 'already') { Write-Fail ("SQLite 加载失败：" + $_.Exception.Message); exit 2 } }

$connection = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$sqlitePath;ReadOnly=True;Version=3;")
$connection.Open()
try {
    # ── 列探测（缺列显式可见，禁以 0 冒充无数据）──
    $columns = [System.Collections.Generic.List[string]]::new()
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = 'PRAGMA table_info([ur_tasks])'
    $reader = $cmd.ExecuteReader()
    while ($reader.Read()) { $columns.Add([string]$reader.GetValue(1)) }
    $reader.Close(); $cmd.Dispose()

    if ($columns.Count -eq 0) { Write-Fail 'ur_tasks 表不存在或无列（禁以空表冒充无数据）'; exit 1 }

    function Resolve-Column {
        param([string[]]$Candidates)
        foreach ($c in $Candidates) { if ($columns -contains $c) { return $c } }
        return $null
    }

    $keyCol = Resolve-Column -Candidates @('NodeKey', 'TaskKey', 'task_key')
    $historyCol = Resolve-Column -Candidates @('HistoryId', 'history_id', 'HistoryKey', 'history_key')
    $parentCol = Resolve-Column -Candidates @('ParentTaskKey', 'parent_task_key', 'ParentKey', 'parent_key')
    $nodesCol = Resolve-Column -Candidates @('RelatedNodes', 'related_nodes')
    $docCol = Resolve-Column -Candidates @('Doc', 'doc')

    if ($null -eq $keyCol) { Write-Fail 'ur_tasks 缺键列（NodeKey/TaskKey）⇒ 无法投影'; exit 1 }

    $applied = [ordered]@{ mount_task_history = 0; mount_subtask = 0; mount_node_task = 0 }
    $skipped = [ordered]@{}
    $rejected = [System.Collections.Generic.List[string]]::new()
    $rowCount = 0

    # 载荷模式：无独立列但存在 Doc ⇒ 从 JSON 载荷取字段（结构事实：ur_tasks = Id/NodeKey/Doc）。
    $payloadMode = ($null -eq $historyCol -and $null -eq $parentCol -and $null -eq $nodesCol -and $null -ne $docCol)
    if ($payloadMode) {
        $skipped['payload-json-mode'] = 1
    }
    else {
        if ($null -eq $historyCol) { $skipped['history-column-missing'] = 1 }
        if ($null -eq $parentCol) { $skipped['parent-column-missing'] = 1 }
        if ($null -eq $nodesCol) { $skipped['related-nodes-column-missing'] = 1 }
    }

    if ($null -ne $historyCol -or $null -ne $parentCol -or $null -ne $nodesCol -or $null -ne $docCol) {
        $select = @("[$keyCol] AS TaskKey")
        if ($null -ne $historyCol) { $select += "[$historyCol] AS HistoryId" }
        if ($null -ne $parentCol) { $select += "[$parentCol] AS ParentTaskKey" }
        if ($null -ne $nodesCol) { $select += "[$nodesCol] AS RelatedNodes" }
        if ($null -ne $docCol) { $select += "[$docCol] AS Doc" }

        $cmd = $connection.CreateCommand()
        $cmd.CommandText = 'SELECT ' + ($select -join ', ') + ' FROM [ur_tasks]'
        $reader = $cmd.ExecuteReader()
        while ($reader.Read()) {
            $taskKey = if ($reader.IsDBNull(0)) { '' } else { [string]$reader.GetValue(0) }
            if ([string]::IsNullOrWhiteSpace($taskKey)) { $rejected.Add('row-without-task-key'); continue }
            $rowCount++

            $i = 1
            $historyId = ''
            if ($null -ne $historyCol) {
                $historyId = if ($reader.IsDBNull($i)) { '' } else { [string]$reader.GetValue($i) }
                $i++
            }
            $parentKey = ''
            if ($null -ne $parentCol) {
                $parentKey = if ($reader.IsDBNull($i)) { '' } else { [string]$reader.GetValue($i) }
                $i++
            }
            $nodes = ''
            if ($null -ne $nodesCol) {
                $nodes = if ($reader.IsDBNull($i)) { '' } else { [string]$reader.GetValue($i) }
                $i++
            }
            if ($null -ne $docCol) {
                $docText = if ($reader.IsDBNull($i)) { '' } else { [string]$reader.GetValue($i) }
                $i++
                if (-not [string]::IsNullOrWhiteSpace($docText)) {
                    try {
                        $doc = $docText | ConvertFrom-Json
                        $h = $doc.PSObject.Properties['HistoryId']
                        if ([string]::IsNullOrWhiteSpace($historyId) -and $null -ne $h) { $historyId = [string]$h.Value }
                        $p = $doc.PSObject.Properties['ParentTaskKey']
                        if ([string]::IsNullOrWhiteSpace($parentKey) -and $null -ne $p) { $parentKey = [string]$p.Value }
                        $n = $doc.PSObject.Properties['RelatedNodes']
                        if ([string]::IsNullOrWhiteSpace($nodes) -and $null -ne $n -and $null -ne $n.Value) { $nodes = (@($n.Value) -join ',') }
                    }
                    catch { $rejected.Add('undecodable-doc:' + $taskKey) }
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($historyId)) { $applied['mount_task_history']++ }
            $isSelfParent = [string]::Equals($parentKey, $taskKey, [StringComparison]::Ordinal)
            if (-not [string]::IsNullOrWhiteSpace($parentKey) -and -not $isSelfParent) { $applied['mount_subtask']++ }
            if (-not [string]::IsNullOrWhiteSpace($nodes)) { $applied['mount_node_task']++ }
        }
        $reader.Close(); $cmd.Dispose()
    }

    # ── 会话投影（session→node）：计数键恒在（0 也出现，禁静默缺面）；表缺失 ⇒ 显式 skipped ──
    $applied['mount_session_node'] = 0
    $sCmd = $connection.CreateCommand()
    $sCmd.CommandText = 'PRAGMA table_info([ur_sessions])'
    $sReader = $sCmd.ExecuteReader()
    $sessionColumns = [System.Collections.Generic.List[string]]::new()
    while ($sReader.Read()) { $sessionColumns.Add([string]$sReader.GetValue(1)) }
    $sReader.Close(); $sCmd.Dispose()

    if ($sessionColumns.Count -eq 0) {
        $skipped['session-table-missing'] = 1
    }
    else {
        $sCmd = $connection.CreateCommand()
        $sCmd.CommandText = 'SELECT * FROM [ur_sessions]'
        $sReader = $sCmd.ExecuteReader()
        $docOrdinal = -1
        try { $docOrdinal = $sReader.GetOrdinal('Doc') } catch { $docOrdinal = -1 }
        while ($sReader.Read()) {
            if ($docOrdinal -lt 0 -or $sReader.IsDBNull($docOrdinal)) { $skipped['session-row-unmappable'] = 1; continue }
            try {
                $sdoc = ([string]$sReader.GetValue($docOrdinal)) | ConvertFrom-Json
                $nk = $sdoc.PSObject.Properties['NodeKeys']
                if ($null -eq $nk) { $nk = $sdoc.PSObject.Properties['RelatedNodes'] }
                if ($null -ne $nk -and $null -ne $nk.Value -and @($nk.Value).Count -gt 0) {
                    $applied['mount_session_node']++
                }
                else { $skipped['session-row-unmappable'] = 1 }
            }
            catch { $skipped['session-row-unmappable'] = 1 }
        }
        $sReader.Close(); $sCmd.Dispose()
    }
}
finally { $connection.Dispose() }

$report = [ordered]@{
    gate = 'graph-rebuild'
    mode = 'dry-run'
    measuredAt = (Get-Date).ToString('o')
    stateRoot = $StateRoot
    sqlite = $sqlitePath
    columns = @($columns)
    counts = [ordered]@{ rows = $rowCount }
    applied = $applied
    skipped = $skipped
    rejected = @($rejected)
    note = '0 写：动作计数 = 将为（Apply 须经 C# 内核 Origin=Rebuild）'
}

if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $dir = Split-Path -Parent $OutFile
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir -Force) }
    ($report | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $OutFile -Encoding UTF8
}

if ($Json) { $report | ConvertTo-Json -Depth 6; exit 0 }

Write-Output ("GraphRebuild(dry-run): history={0} subtask={1} node={2} session={3} skipped={4} rejected={5}" -f `
    $applied['mount_task_history'], $applied['mount_subtask'], $applied['mount_node_task'], $applied['mount_session_node'], $skipped.Count, $rejected.Count)
foreach ($k in $skipped.Keys) { Write-Output ("  · skipped {0}" -f $k) }
foreach ($r in $rejected) { Write-Output ("  x rejected {0}" -f $r) }
exit 0
