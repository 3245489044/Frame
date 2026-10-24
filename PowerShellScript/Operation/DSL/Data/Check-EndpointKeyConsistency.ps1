#requires -Version 5.1
<#
.SYNOPSIS
    端点键一致性门禁（EndpointKeyConsistency）—— 实体键 ⊆ 图边端点集合，且端点 Id 是构造器不动点。
.DESCRIPTION
    根因（2026-10-25 node/task 加载失败）：写边端点与实体主键不是同一套键空间 ——
      history 端点三重前缀（history:history.history:…）、task 会话端点（task:history:*）、
      幻影 task（task:session）、容器行被当历史根。
    本门禁把「键空间自洽」固化为可判定判据（只读，零写）：
      G1 history_owns_task.source.Id 必须等于某历史根经 History(rootKey) 派生的端点 Id；
      G2 cross_db_link.source.Id 必须是树可达的成员 task（history_owns_task.target 集）；
      G3 端点 Id 必须是不动点（history: History(Id)==Id；session: Session(Id)==Id；node: 禁 x.x. 重复 wf）；
      G4 session_owns.source 同 G3（会话端点）。
    基线棘轮（Baselines\EndpointKeyConsistency.psd1）：现存违规登记为已知项 ⇒ 只降不升（新键即红）。
    负对照（-NegativeControl）：合成三类违规样本，判据必须命中且合法样本不得误报（防判据恒真/恒假）。
    缺库 / 缺驱动 ⇒ [SKIP] 退出 0（显式不适用，不伪造通过）。
.PARAMETER Check
    判定模式：违规（超出基线）→ 退出码 1；缺省报告模式。
.PARAMETER NegativeControl
    跑判据正/负对照（门禁登记为必需）。
.PARAMETER StateRoot
    数据根（缺省：WF_DATA_ROOT → runtime.json.data → <repo>\Data）。
.PARAMETER DllDir
    驱动目录（缺省探测 Projects\FileDriven\BuildOutput\{Debug,Release}）。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl,
    [string]$StateRoot = '',
    [string]$DllDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name EndpointKeyConsistency

Write-GateHeader '端点键一致性（EndpointKeyConsistency）'
$violations = [System.Collections.Generic.List[string]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

function Write-Skip {
    param([string]$Reason)
    Write-Host "  [SKIP] $Reason" -ForegroundColor DarkGray
    exit 0
}

# ── 判据原语（与 NodeEndpointIds.History / SessionRoot 同构；负对照覆盖）──
function Strip-Once {
    param([string]$Value, [string]$Prefix)
    if (-not [string]::IsNullOrEmpty($Prefix) -and $Value.Length -gt $Prefix.Length -and
        $Value.StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
        return $Value.Substring($Prefix.Length)
    }
    return $Value
}

function Get-HistoryIdentity {
    param([string]$Id)
    $v = $Id
    for ($i = 0; $i -lt 4; $i++) {
        $n = Strip-Once (Strip-Once $v 'history:') 'history.'
        if ($n -eq $v) { break }
        $v = $n
    }
    return $v
}

function Get-HistoryEndpointId { param([string]$Id) return 'history.' + (Get-HistoryIdentity $Id) }

function Get-SessionIdentity {
    param([string]$Id)
    $v = $Id
    for ($i = 0; $i -lt 4; $i++) {
        $n = Strip-Once (Strip-Once $v 'session:') 'session.'
        if ($n -eq $v) { break }
        $v = $n
    }
    return $v
}

function Get-SessionEndpointId { param([string]$Id) return 'session.' + (Get-SessionIdentity $Id) }

function Test-DuplicateWorkflowPrefix {
    param([string]$Id)
    return [regex]::IsMatch($Id, '^(?<wf>[^.]+)\.\k<wf>\.')
}

function Get-EndpointIdFromUniqueKey {
    param([string]$UniqueKey, [string]$Type)
    return Strip-Once $UniqueKey ("$Type" + ':')
}

# ── 依赖与数据根（缺 ⇒ SKIP，不伪造通过）──
if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = @(
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'System.Data.SQLite.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($DllDir) -or -not (Test-Path -LiteralPath $DllDir)) {
    Write-Skip '未找到 System.Data.SQLite.dll（先构建 FileDriven 或用 -DllDir）'
}
$DllDir = (Resolve-Path -LiteralPath $DllDir).Path
foreach ($dll in @('System.Data.SQLite.dll', 'LiteDB.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $DllDir $dll))) { Write-Skip "缺依赖：$dll" }
}

if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:WF_DATA_ROOT)) { $StateRoot = $env:WF_DATA_ROOT }
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
if (-not (Test-Path -LiteralPath $StateRoot)) { Write-Skip "状态根不存在：$StateRoot" }
$StateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
$agentDb = Join-Path $StateRoot 'agent.db'
$graphDb = Join-Path $StateRoot 'graph.db'

function Import-Assembly {
    param([string]$Path)
    try { Add-Type -Path $Path -ErrorAction Stop }
    catch { if ($_.Exception.Message -notmatch 'already') { throw } }
}

function Read-Histories {
    param([string]$DbPath)
    Import-Assembly (Join-Path $DllDir 'System.Data.SQLite.dll')
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;ReadOnly=True;Version=3;")
    $cn.Open()
    try {
        $c = $cn.CreateCommand(); $c.CommandText = 'SELECT NodeKey, Doc FROM [ur_histories]'
        $r = $c.ExecuteReader()
        $rows = @()
        while ($r.Read()) {
            $rows += [pscustomobject]@{ Key = $r.GetString(0); Doc = $(if ($r.IsDBNull(1)) { '' } else { $r.GetString(1) }) }
        }
        $r.Close()
        return $rows
    } finally { $cn.Dispose() }
}

function Read-TaskKeys {
    param([string]$DbPath)
    Import-Assembly (Join-Path $DllDir 'System.Data.SQLite.dll')
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;ReadOnly=True;Version=3;")
    $cn.Open()
    try {
        $c = $cn.CreateCommand(); $c.CommandText = 'SELECT NodeKey FROM [ur_tasks]'
        $r = $c.ExecuteReader()
        $keys = @()
        while ($r.Read()) { $keys += $r.GetString(0) }
        $r.Close()
        return $keys
    } finally { $cn.Dispose() }
}

function Get-EndpointType {
    param([string]$UniqueKey)
    if ($UniqueKey -match '^([A-Za-z_][A-Za-z0-9_]*):') { return $Matches[1] }
    return ''
}

function Read-Edges {
    param([string]$DbPath)
    Import-Assembly (Join-Path $DllDir 'LiteDB.dll')
    try { $db = New-Object LiteDB.LiteDatabase("Filename=$DbPath;ReadOnly=true") }
    catch { Write-Skip "图库被占用（关闭宿主后重跑）：$($_.Exception.Message)" }
    try {
        $col = $db.GetCollection('ur_references')
        $edges = @()
        foreach ($d in $col.FindAll()) {
            $skey = [string]$d['SourceId'].AsString
            $tkey = [string]$d['TargetId'].AsString
            $edges += [pscustomobject]@{
                SourceType = (Get-EndpointType $skey)
                SourceKey  = $skey
                TargetType = (Get-EndpointType $tkey)
                TargetKey  = $tkey
                RefType    = [string]$d['RefType'].AsString
            }
        }
        return $edges
    } finally { $db.Dispose() }
}

if (-not (Test-Path -LiteralPath $agentDb)) { Write-Skip "agent.db 不存在：$agentDb" }
if (-not (Test-Path -LiteralPath $graphDb)) { Write-Skip "graph.db 不存在：$graphDb" }

$histories = @(Read-Histories -DbPath $agentDb)
$taskKeys = @(Read-TaskKeys -DbPath $agentDb)
$edges = @(Read-Edges -DbPath $graphDb)
$notes.Add("数据：histories=$($histories.Count) tasks=$($taskKeys.Count) edges=$($edges.Count)")

# ── 负对照（防判据恒真/恒假）──
if ($NegativeControl) {
    $nc = [System.Collections.Generic.List[string]]::new()
    if ((Get-HistoryEndpointId 'history.history:242e') -eq 'history.history:242e') { $nc.Add('history 三重前缀未被判出') }
    if ((Get-HistoryEndpointId 'history.242e') -ne 'history.242e') { $nc.Add('history 合法端点被误判') }
    if ((Get-SessionEndpointId 'session.session.session.x') -eq 'session.session.session.x') { $nc.Add('session 重复前缀未被判出') }
    if ((Get-SessionEndpointId 'session.history:242e') -ne 'session.history:242e') { $nc.Add('session 合法端点被误判') }
    if (-not (Test-DuplicateWorkflowPrefix 'agent-round.agent-round.state_response')) { $nc.Add('node 重复 wf 未被判出') }
    if (Test-DuplicateWorkflowPrefix 'agent-round.state_response@history:242e') { $nc.Add('node 合法端点被误判') }
    foreach ($m in $nc) { $violations.Add("[NC] $m") }
}

# ── G1：history 源端点 ∈ 历史根派生端点集 ──
$rootEndpointIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($h in $histories) {
    if ($h.Doc -notmatch '^\[Record\]\s+history_root:') { continue }
    [void]$rootEndpointIds.Add((Get-HistoryEndpointId $h.Key))
}
$g1 = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$memberTaskKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($e in $edges) {
    $sid = Get-EndpointIdFromUniqueKey $e.SourceKey $e.SourceType
    $tid = Get-EndpointIdFromUniqueKey $e.TargetKey $e.TargetType
    if ($e.RefType -eq 'history_owns_task') {
        if ($e.TargetType -eq 'task') { [void]$memberTaskKeys.Add($tid) }
        if ($e.SourceType -eq 'history' -and -not $rootEndpointIds.Contains($sid)) { [void]$g1.Add($sid) }
    }
}

# ── G2：cross_db_link 的 task 源 ∈ 成员 task（树可达；幻影/会话任务即违规）──
$g2 = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$taskSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($k in $taskKeys) { [void]$taskSet.Add($k) }
foreach ($e in $edges) {
    if ($e.RefType -ne 'cross_db_link' -or $e.SourceType -ne 'task') { continue }
    $sid = Get-EndpointIdFromUniqueKey $e.SourceKey $e.SourceType
    if (-not $taskSet.Contains($sid) -or -not $memberTaskKeys.Contains($sid)) { [void]$g2.Add($sid) }
}

# ── G3：端点 Id 不动点（history / session / node）──
$g3 = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$g4 = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($e in $edges) {
    foreach ($side in @(
        [pscustomobject]@{ Type = $e.SourceType; Key = $e.SourceKey },
        [pscustomobject]@{ Type = $e.TargetType; Key = $e.TargetKey })) {
        if ([string]::IsNullOrWhiteSpace($side.Type) -or [string]::IsNullOrWhiteSpace($side.Key)) { continue }
        $id = Get-EndpointIdFromUniqueKey $side.Key $side.Type
        switch ($side.Type) {
            'history' { if ((Get-HistoryEndpointId $id) -ne $id) { [void]$g3.Add($id) } }
            'session' { if ((Get-SessionEndpointId $id) -ne $id) { [void]$g3.Add($id) } }
            'node' { if (Test-DuplicateWorkflowPrefix $id) { [void]$g3.Add($id) } }
        }
    }
    if ($e.RefType -eq 'session_owns' -and $e.SourceType -eq 'session') {
        $sid = Get-EndpointIdFromUniqueKey $e.SourceKey $e.SourceType
        if ((Get-SessionEndpointId $sid) -ne $sid) { [void]$g4.Add($sid) }
    }
}

# ── 基线棘轮：新键即红；基线中已消失的键提示收紧 ──
function Add-Ratchet {
    param([string]$Name, $Current)
    $allowed = @()
    if ($baseline.ContainsKey($Name)) { $allowed = @($baseline[$Name] | ForEach-Object { [string]$_ }) }
    $allowedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($a in $allowed) { [void]$allowedSet.Add($a) }
    foreach ($k in $Current) {
        if (-not $allowedSet.Contains($k)) { $violations.Add("[$Name] 未登记端点键：$k（新增违规 ⇒ 只降不升）") }
    }
    $disappeared = @($allowed | Where-Object { -not $Current.Contains($_) })
    if ($disappeared.Count -gt 0) { $notes.Add("[$Name] 基线可收紧：已消失 $($disappeared.Count) 项（$($disappeared -join ', ')）") }
}

Add-Ratchet -Name 'G1' -Current $g1
Add-Ratchet -Name 'G2' -Current $g2
Add-Ratchet -Name 'G3' -Current $g3
Add-Ratchet -Name 'G4' -Current $g4

foreach ($n in $notes) { Write-Host "  [INFO] $n" -ForegroundColor DarkGray }
Write-Host ("  [INFO] roots={0} history_edges={1} g1={2} g2={3} g3={4} g4={5}" -f `
    $rootEndpointIds.Count, $memberTaskKeys.Count, $g1.Count, $g2.Count, $g3.Count, $g4.Count) -ForegroundColor DarkGray

Write-GateVerdict -Violations $violations -Check:$Check -Key 'EndpointKeyConsistency'

