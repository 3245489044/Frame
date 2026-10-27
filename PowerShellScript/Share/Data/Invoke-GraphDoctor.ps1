#requires -Version 5.1
<#
.SYNOPSIS
    图关系健康体检（只读；0 库写 / 0 裁决）—— 端点/边/声明/墓碑计数 + 实体差集 + 库文件诊断。

.DESCRIPTION
    判据载体（全部结构事实，禁执行者叙述）：
      · SQLite 实体面计数（ur_tasks/ur_nodes/ur_histories/ur_sessions/ixn_edges/ixn_nodes；表缺失 ⇒ null，禁以 0 冒充）
      · LiteDB 关系面计数（ur_endpoints/ur_references/ur_relations/ur_graph_tombstones；墓碑按 Kind 分组）
      · 边键遗留形态（_id 含 U+0001）与不可解码行（Doc 缺失 / 非字符串）
      · 实体键 ↔ 端点键差集（按 ResourceType 映射，映射不到的显式列出，禁静默丢弃）
      · 同目录 *.db 清单 + 大小 / mtime / 可打开性（graph-log.db 损坏 ⇒ openError 可见）
    纪律：默认只写 stdout；-OutFile 才落盘（诊断产物，只增不改）。禁改库、禁裁决。
    退出码：0 成功 / 1 状态根或库缺失 / 2 依赖缺失 / 3 图库被独占（SQLite 部分仍输出）

.PARAMETER StateRoot
    状态根（缺省按 WF_DATA_ROOT → FILEDRIVEN_DATA_ROOT → ROUTE_STATE_ROOT → runtime.json.data → <repo>\Data 解析）。

.PARAMETER DllDir
    含 System.Data.SQLite.dll / LiteDB.dll 的构建输出目录（缺省自动探测）。

.PARAMETER Json
    结构化 JSON 输出（缺省输出人读摘要；-OutFile 恒写 JSON）。

.PARAMETER OutFile
    报告落盘路径（缺省不落盘：诊断期 0 写）。

.PARAMETER SelfTest
    正/负对照自检（不触碰任何库）：Legacy 键检出、缺失表 null 语义、裸身份归一。

.EXAMPLE
    .\Invoke-GraphDoctor.ps1 -Json

.EXAMPLE
    .\Invoke-GraphDoctor.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [switch]$Json,
    [string]$OutFile = '',
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[GraphDoctor] $Message") }

# ── 纯函数（自检与主体共用；单一实现）──

# 遗留边键：U+0001 分隔形态（旧写路径产物；只读识别，新写禁止）。
function Test-LegacyEdgeId {
    param([string]$Id)
    if ([string]::IsNullOrEmpty($Id)) { return $false }
    return ($Id.IndexOf([char]1) -ge 0)
}

# 裸身份归一：剥净 history: / history. / session: / session.（幂等；与 NodeEndpointIds.NormalizeHistoryId 同口径）。
function Get-BareId {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim()
    while ($true) {
        if ($v.StartsWith('history:') -or $v.StartsWith('session:')) { $v = $v.Substring(8); continue }
        if ($v.StartsWith('history.') -or $v.StartsWith('session.')) { $v = $v.Substring(8); continue }
        break
    }
    return $v
}

# 表缺失 / 不可解码的显式三态：值存在 ⇒ 值；不存在 ⇒ $null（禁以 0 冒充"空表"）。
function ConvertTo-CountOrNull {
    param($Value)
    if ($null -eq $Value) { return $null }
    return [int]$Value
}

if ($SelfTest) {
    $fail = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-LegacyEdgeId -Id ("a" + [char]1 + "b" + [char]1 + "c"))) { $fail.Add('负对照失效：U+0001 分隔键未被检出') }
    if (Test-LegacyEdgeId -Id 'task:n::node:x::cross_db_link') { $fail.Add('正对照失效：规范 :: 键被误判为 Legacy') }
    if (Test-LegacyEdgeId -Id '') { $fail.Add('正对照失效：空键被误判为 Legacy') }
    if ((Get-BareId 'history:history.0001') -ne '0001') { $fail.Add('裸身份归一失效：history:history.0001') }
    if ((Get-BareId 'history.0001') -ne '0001') { $fail.Add('裸身份归一失效：history.0001') }
    if ((Get-BareId '0001') -ne '0001') { $fail.Add('裸身份归一失效：裸值被改写') }
    if ($null -ne (ConvertTo-CountOrNull $null)) { $fail.Add('三态失效：缺失被压平为值') }
    if ((ConvertTo-CountOrNull 0) -ne 0) { $fail.Add('三态失效：0 被当作缺失') }
    if ($fail.Count -gt 0) {
        Write-Fail ("SelfTest FAIL：" + ($fail -join ' | '))
        exit 1
    }
    Write-Host 'GraphDoctor SelfTest: OK（Legacy 检出 / 裸身份归一 / 缺失表 null 三态 均有正负对照）'
    exit 0
}

# ── 1) 状态根解析（与 Read-AgentDb.ps1 / AgentStateLocator 同源口径）──
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
$agentDb = Join-Path $StateRoot 'agent.db'
$graphDb = Join-Path $StateRoot 'graph.db'

# ── 2) 依赖 DLL 定位（-DllDir 可覆盖；缺失 ⇒ 退出码 2）──
if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = @(
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release'),
        (Join-Path $repoRoot 'Environment\CentralTest\BuildOutput\Debug\net10.0-windows'),
        (Join-Path $repoRoot 'BuildOutput\publish-smoke-host')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LiteDB.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($DllDir) -or -not (Test-Path -LiteralPath $DllDir)) {
    Write-Fail '未找到 LiteDB.dll；请先构建宿主输出或用 -DllDir 指定（缺依赖 ⇒ 退出码 2）。'
    exit 2
}
$DllDir = (Resolve-Path -LiteralPath $DllDir).Path
$sqliteDll = Join-Path $DllDir 'System.Data.SQLite.dll'
$liteDll = Join-Path $DllDir 'LiteDB.dll'
if (-not (Test-Path -LiteralPath $liteDll)) { Write-Fail "缺依赖：$liteDll（退出码 2）"; exit 2 }
if (-not (Test-Path -LiteralPath $sqliteDll)) { Write-Fail "缺依赖：$sqliteDll（退出码 2）"; exit 2 }

function Import-Assembly {
    param([string]$Path)
    try { Add-Type -Path $Path -ErrorAction Stop }
    catch { if ($_.Exception.Message -notmatch 'already') { throw } }
}

# ── 3) SQLite 实体面（只读；缺失表 ⇒ null，禁以 0 冒充）──
function Get-SqliteEntityKeys {
    param($Connection, [string]$Table)
    try {
        $c = $Connection.CreateCommand(); $c.CommandText = "SELECT [NodeKey] FROM [$Table]"
        try {
            $r = $c.ExecuteReader(); $keys = [System.Collections.Generic.List[string]]::new()
            while ($r.Read()) { if (-not $r.IsDBNull(0)) { $keys.Add([string]$r.GetValue(0)) } }
            $r.Close()
            return $keys
        } finally { $c.Dispose() }
    } catch { return $null }
}

function Read-SqliteEntitySurface {
    param([string]$DbPath)
    $out = [ordered]@{
        path = $DbPath; exists = (Test-Path -LiteralPath $DbPath); error = $null
        counts = [ordered]@{}; missingTables = @(); entityKeys = [ordered]@{}
    }
    if (-not $out.exists) { return $out }
    try {
        Import-Assembly $sqliteDll
        $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;ReadOnly=True;Version=3;")
        $cn.Open()
        try {
            foreach ($t in @('ur_tasks', 'ur_nodes', 'ur_histories', 'ur_sessions', 'ixn_edges', 'ixn_nodes')) {
                try {
                    $c = $cn.CreateCommand(); $c.CommandText = "SELECT COUNT(*) FROM [$t]"
                    $out.counts[$t] = [int]$c.ExecuteScalar(); $c.Dispose()
                } catch {
                    $out.counts[$t] = $null
                    $out.missingTables += $t
                }
            }
            foreach ($t in @('ur_tasks', 'ur_nodes', 'ur_histories', 'ur_sessions')) {
                $out.entityKeys[$t] = Get-SqliteEntityKeys -Connection $cn -Table $t
            }
        } finally { $cn.Dispose() }
    } catch { $out.error = $_.Exception.Message }
    return $out
}

# ── 4) LiteDB 关系面（只读；打不开 ⇒ openError 可见，不抛）──
function Open-LiteReadOnly {
    param([string]$DbPath)
    Import-Assembly $liteDll
    return New-Object LiteDB.LiteDatabase("Filename=$DbPath;ReadOnly=true")
}

function Get-LiteCount {
    param($Db, [string]$Name)
    try { return [int]$Db.GetCollection($Name).Count() } catch { return $null }
}

function Test-DocDecodable {
    param($Doc)
    try {
        if (-not $Doc.ContainsKey('Doc')) { return $false }
        $v = $Doc['Doc']
        if ($null -eq $v) { return $false }
        if (-not $v.IsString) { return $false }
        return -not [string]::IsNullOrWhiteSpace([string]$v.AsString)
    } catch { return $false }
}

function Get-BsonText {
    param($Doc, [string]$Name)
    try {
        if (-not $Doc.ContainsKey($Name)) { return $null }
        $v = $Doc[$Name]
        if ($null -eq $v) { return $null }
        if ($v.IsString) { return [string]$v.AsString }
        return [string]$v.ToString()
    } catch { return $null }
}

function Read-LiteRelationSurface {
    param([string]$DbPath)
    $out = [ordered]@{
        path = $DbPath; exists = (Test-Path -LiteralPath $DbPath); bytes = $null; openError = $null
        counts = [ordered]@{}; tombstonesByKind = [ordered]@{}; endpointShapeHistogram = [ordered]@{}
        endpointIdsByType = @{}; legacyEdgeIds = 0; legacyEdgeSamples = @()
        undecodableRows = 0; undecodableSamples = @(); locked = $false
    }
    if (-not $out.exists) { return $out }
    $out.bytes = (Get-Item -LiteralPath $DbPath).Length
    $db = $null
    try { $db = Open-LiteReadOnly -DbPath $DbPath }
    catch {
        $out.openError = $_.Exception.Message
        $out.locked = ($_.Exception.Message -match 'lock|Lock|占用|in use')
        return $out
    }
    try {
        foreach ($name in @('ur_endpoints', 'ur_references', 'ur_relations', 'ur_graph_tombstones')) {
            $out.counts[$name] = Get-LiteCount -Db $db -Name $name
        }
        # 端点：_id = {type}:{id}；产出差集所需 type→ids 桶与形态直方图。
        foreach ($d in $db.GetCollection('ur_endpoints').FindAll()) {
            $key = Get-BsonText -Doc $d -Name '_id'
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $i = $key.IndexOf(':')
            $type = if ($i -gt 0) { $key.Substring(0, $i) } else { '' }
            $id = if ($i -gt 0) { $key.Substring($i + 1) } else { $key }
            if (-not $out.endpointIdsByType.ContainsKey($type)) {
                $out.endpointIdsByType[$type] = [System.Collections.Generic.List[string]]::new()
            }
            $out.endpointIdsByType[$type].Add($id)
            $shape = if ($id.Contains('.')) { ($id.Split('.')[0]) + '.*' } else { '*' }
            $bucket = "$type`:$shape"
            $prev = if ($out.endpointShapeHistogram.Contains($bucket)) { $out.endpointShapeHistogram[$bucket] } else { 0 }
            $out.endpointShapeHistogram[$bucket] = 1 + $prev
        }
        # 边：遗留分隔符（U+0001）与不可解码行（Doc 缺失/非字符串）分开计数。
        foreach ($d in $db.GetCollection('ur_references').FindAll()) {
            $key = Get-BsonText -Doc $d -Name '_id'
            if (Test-LegacyEdgeId -Id $key) {
                $out.legacyEdgeIds++
                if ($out.legacyEdgeSamples.Count -lt 5) { $out.legacyEdgeSamples += ($key -replace [string][char]1, '<U+0001>') }
            }
            if (-not (Test-DocDecodable -Doc $d)) {
                $out.undecodableRows++
                if ($out.undecodableSamples.Count -lt 5) { $out.undecodableSamples += $key }
            }
        }
        # 墓碑：按 Kind 分组（删除意图分布）。
        foreach ($d in $db.GetCollection('ur_graph_tombstones').FindAll()) {
            $kind = Get-BsonText -Doc $d -Name 'Kind'
            if ([string]::IsNullOrWhiteSpace($kind)) { $kind = '(unknown)' }
            $prev = if ($out.tombstonesByKind.Contains($kind)) { $out.tombstonesByKind[$kind] } else { 0 }
            $out.tombstonesByKind[$kind] = 1 + $prev
        }
    } catch { $out.openError = $_.Exception.Message }
    finally { $db.Dispose() }
    return $out
}

# ── 5) 实体 ↔ 端点差集（映射规则显式；映射不到不静默丢弃）──
$EntityMappings = @(
    [pscustomobject]@{ ResourceType = 'task';    Table = 'ur_tasks';     Kind = 'Exact' },
    [pscustomobject]@{ ResourceType = 'node';    Table = 'ur_nodes';     Kind = 'NodeSuffix' },
    [pscustomobject]@{ ResourceType = 'history'; Table = 'ur_histories'; Kind = 'BareIdentity' },
    [pscustomobject]@{ ResourceType = 'session'; Table = 'ur_sessions';  Kind = 'BareIdentity' }
)

function Test-EntityMatched {
    param([string]$Kind, [string]$NodeKey, [string]$EndpointId)
    switch ($Kind) {
        'Exact' { return ($EndpointId -eq $NodeKey) }
        'NodeSuffix' {
            if ($EndpointId -eq $NodeKey) { return $true }
            return $EndpointId.EndsWith('.' + $NodeKey, [StringComparison]::Ordinal)
        }
        default { return ((Get-BareId $EndpointId) -eq (Get-BareId $NodeKey)) }
    }
}

function Get-EntityEndpointDiff {
    param($SqliteSurface, $GraphSurface)
    $result = [ordered]@{
        perType = [ordered]@{}
        endpointsMinusEntities = [ordered]@{ count = 0; samples = @() }
        unknownTypes = @()
    }
    if ($null -eq $GraphSurface -or $GraphSurface.openError) { return $result }

    $matchedEndpointIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($m in $EntityMappings) {
        $keys = $SqliteSurface.entityKeys[$m.Table]
        $endpointIds = @()
        if ($GraphSurface.endpointIdsByType.ContainsKey($m.ResourceType)) {
            $endpointIds = @($GraphSurface.endpointIdsByType[$m.ResourceType])
        }
        if ($null -eq $keys) {
            $result.perType[$m.ResourceType] = [ordered]@{
                entityKeys = $null; endpoints = $endpointIds.Count; matched = $null
                unmatchedEntitySamples = @(); missingTable = $m.Table
            }
            continue
        }
        $unmatched = @()
        $matched = 0
        foreach ($k in $keys) {
            $hit = $false
            foreach ($id in $endpointIds) {
                if (Test-EntityMatched -Kind $m.Kind -NodeKey $k -EndpointId $id) {
                    $hit = $true; [void]$matchedEndpointIds.Add($id); break
                }
            }
            if ($hit) { $matched++ } elseif ($unmatched.Count -lt 20) { $unmatched += $k }
        }
        $result.perType[$m.ResourceType] = [ordered]@{
            entityKeys = $keys.Count; endpoints = $endpointIds.Count; matched = $matched
            unmatchedEntitySamples = $unmatched; missingTable = $null
        }
    }

    $mappedTypes = @($EntityMappings | ForEach-Object { $_.ResourceType })
    foreach ($type in $GraphSurface.endpointIdsByType.Keys) {
        if ($mappedTypes -notcontains $type) { $result.unknownTypes += $type; continue }
        foreach ($id in $GraphSurface.endpointIdsByType[$type]) {
            if (-not $matchedEndpointIds.Contains($id)) {
                $result.endpointsMinusEntities.count++
                if ($result.endpointsMinusEntities.samples.Count -lt 20) {
                    $result.endpointsMinusEntities.samples += "$type`:$id"
                }
            }
        }
    }
    return $result
}

# ── 6) 同目录库清单（判据载体 = 文件事实 + 可打开性，非叙述）──
function Get-GraphFiles {
    param([string]$Root)
    $files = @()
    foreach ($f in (Get-ChildItem -LiteralPath $Root -Filter '*.db' -File -ErrorAction SilentlyContinue | Sort-Object Name)) {
        $entry = [ordered]@{
            file = $f.Name; active = ($f.Name -in @('agent.db', 'graph.db')); bytes = $f.Length
            lastWriteUtc = $f.LastWriteTimeUtc.ToString('o'); openError = $null; locked = $false
        }
        if ($f.Name -ne 'agent.db') {
            try { $probe = Open-LiteReadOnly -DbPath $f.FullName; $probe.Dispose() }
            catch {
                $entry.openError = $_.Exception.Message
                $entry.locked = ($_.Exception.Message -match 'lock|Lock|占用|in use')
            }
        }
        $files += [pscustomobject]$entry
    }
    return $files
}

# ── 7) 主流程（0 裁决：只输出事实；rc 仅表达可读性）──
$report = [ordered]@{
    schema = 'graph-health-v1'
    tsUtc = (Get-Date).ToUniversalTime().ToString('o')
    stateRoot = $StateRoot
    sqlite = $null; graph = $null; diff = $null; graphFiles = @()
}

$rc = 0
$report.sqlite = Read-SqliteEntitySurface -DbPath $agentDb
$report.graph = Read-LiteRelationSurface -DbPath $graphDb
$report.diff = Get-EntityEndpointDiff -SqliteSurface $report.sqlite -GraphSurface $report.graph
$report.graphFiles = Get-GraphFiles -Root $StateRoot

if ($report.sqlite.error) { Write-Fail "SQLite 读取失败：$($report.sqlite.error)"; $rc = 1 }
if ($report.graph.openError -and -not $report.graph.locked) {
    Write-Fail "图库打开失败：$($report.graph.openError)"
    if ($rc -eq 0) { $rc = 1 }
}
if ($report.graph.locked) {
    Write-Fail '图库被独占（LiteDB 单 owner）：请先关闭宿主进程后重跑；运行中系统请用 UI 图库页。'
    if ($rc -eq 0) { $rc = 3 }
}
if (-not $report.sqlite.exists -or -not $report.graph.exists) {
    Write-Fail '库缺失：agent.db 或 graph.db 不存在（退出码 1）'
    if ($rc -eq 0) { $rc = 1 }
}

$reportJson = ConvertTo-Json -InputObject $report -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $dir = Split-Path -Parent $OutFile
    if (-not [string]::IsNullOrWhiteSpace($dir) -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    [IO.File]::WriteAllText($OutFile, $reportJson, [Text.UTF8Encoding]::new($false))
    Write-Host "[GraphDoctor] 报告已写入：$OutFile"
}

if ($Json) {
    $reportJson
} else {
    Write-Host ''
    Write-Host '== 图关系健康体检（只读）==' -ForegroundColor Cyan
    Write-Host ("状态根：{0}" -f $StateRoot)
    $sqliteText = ($report.sqlite.counts.GetEnumerator() | ForEach-Object {
        $v = if ($null -eq $_.Value) { '缺失' } else { $_.Value }
        "$($_.Key)=$v"
    }) -join '  '
    $graphText = ($report.graph.counts.GetEnumerator() | ForEach-Object {
        $v = if ($null -eq $_.Value) { '缺失' } else { $_.Value }
        "$($_.Key)=$v"
    }) -join '  '
    $tombText = ($report.graph.tombstonesByKind.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '  '
    Write-Host ("SQLite 实体：{0}" -f $sqliteText)
    Write-Host ("LiteDB 关系：{0}" -f $graphText)
    Write-Host ("墓碑分布：{0}" -f $tombText)
    Write-Host ("遗留边键={0}  不可解码行={1}" -f $report.graph.legacyEdgeIds, $report.graph.undecodableRows)
    foreach ($t in $report.diff.perType.Keys) {
        $p = $report.diff.perType[$t]
        $entityText = if ($null -eq $p.entityKeys) { '缺失' } else { $p.entityKeys }
        $matchedText = if ($null -eq $p.matched) { '不可判定' } else { $p.matched }
        Write-Host ("差集[{0}]：实体={1} 端点={2} 命中={3}" -f $t, $entityText, $p.endpoints, $matchedText)
    }
    Write-Host ("孤儿端点（端点−实体）={0}" -f $report.diff.endpointsMinusEntities.count)
    foreach ($f in $report.graphFiles) {
        $state = if ($f.openError) { "打不开：$($f.openError)" } else { '可读' }
        Write-Host ("文件 {0}  bytes={1}  {2}" -f $f.file, $f.bytes, $state)
    }
    if ($report.graph.openError) { Write-Host ("图库 openError：{0}" -f $report.graph.openError) -ForegroundColor Yellow }
    Write-Host '（机器可读明细：加 -Json 或 -OutFile <path>）'
}

exit $rc

