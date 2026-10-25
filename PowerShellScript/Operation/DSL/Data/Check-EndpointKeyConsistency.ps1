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
    [string]$DllDir = '',
    [string]$ModulesDir = ''
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

# ── 判据原语（唯一来源 = NodeEndpointIds；本脚本不留第二套归一实现）──
function Get-HistoryIdentity {
    param([string]$Id)
    return [Modules.Node.Identity.NodeEndpointIds]::NormalizeHistoryId($Id)
}

function Get-HistoryEndpointId {
    param([string]$Id)
    try { return [Modules.Node.Identity.NodeEndpointIds]::History($Id) } catch { return '' }
}

function Get-SessionIdentity {
    param([string]$Id)
    return [Modules.Node.Identity.NodeEndpointIds]::NormalizeSessionId($Id)
}

function Get-SessionEndpointId {
    param([string]$Id)
    try { return [Modules.Node.Identity.NodeEndpointIds]::Session($Id) } catch { return '' }
}

# 已知工作流集（默认 + 数据面 + 基线）：重复 wf 判据的载体（非执行者叙述）。
$script:knownWorkflowIds = @('agent-round')
if ($baseline.KnownWorkflowIds.Value) {
    $script:knownWorkflowIds += @($baseline.KnownWorkflowIds.Value | ForEach-Object { [string]$_ })
}
$script:knownWorkflowIds = @($script:knownWorkflowIds | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -Unique)

function Test-DuplicateWorkflowPrefix {
    param([string]$Id)
    $value = ($Id -split '@', 2)[0]
    $segments = $value -split '\.'
    foreach ($wf in $script:knownWorkflowIds) {
        $count = @($segments | Where-Object { $_ -eq $wf }).Count
        if ($count -gt 1) { return $true }
    }
    return $false
}

function Get-EndpointIdFromUniqueKey {
    param([string]$UniqueKey, [string]$Type)
    $prefix = "$Type" + ':'
    if (-not [string]::IsNullOrEmpty($Type) -and $UniqueKey.Length -gt $prefix.Length -and
        $UniqueKey.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
        return $UniqueKey.Substring($prefix.Length)
    }
    return $UniqueKey
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

# 判定原语单源：必须复用产品程序集（禁第二套归一文法）；缺失 ⇒ -Check 红 / 报告模式 SKIP。
$canonicalDlls = @('Modules.Parsing.Keys.dll', 'Modules.KeySpace.dll', 'Modules.Node.dll')
if ([string]::IsNullOrWhiteSpace($ModulesDir)) {
    $ModulesDir = @(
        (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Release'),
        (Join-Path $repoRoot 'Environment\CentralTest\CSharpTest\BuildOutput\Debug'),
        (Join-Path $repoRoot 'BuildOutput\publish-smoke-agent')
    ) | Where-Object {
        $dir = $_
        (Test-Path -LiteralPath $dir -PathType Container) -and
        -not (@($canonicalDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $dir $_)) }).Count)
    } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($ModulesDir) -or -not (Test-Path -LiteralPath $ModulesDir)) {
    $msg = "缺判定原语程序集（$($canonicalDlls -join ', ')）；先构建 Frame 或用 -ModulesDir 指定。禁静默回退 PS 镜像。"
    if ($Check) {
        $violations.Add("PRIMITIVE_UNAVAILABLE $msg")
        Write-GateVerdict -Violations $violations -Check:$true -Key 'EndpointKeyConsistency'
    }
    Write-Skip $msg
}
$ModulesDir = (Resolve-Path -LiteralPath $ModulesDir).Path
foreach ($dll in $canonicalDlls) {
    if (-not (Test-Path -LiteralPath (Join-Path $ModulesDir $dll))) {
        $msg = "缺判定原语程序集：$dll（目录 $ModulesDir）"
        if ($Check) {
            $violations.Add("PRIMITIVE_UNAVAILABLE $msg")
            Write-GateVerdict -Violations $violations -Check:$true -Key 'EndpointKeyConsistency'
        }
        Write-Skip $msg
    }
}
try {
    foreach ($dll in $canonicalDlls) { Import-Assembly (Join-Path $ModulesDir $dll) }
} catch {
    $msg = "判定原语程序集加载失败：$($_.Exception.Message)（禁回退 PS 镜像）"
    if ($Check) {
        $violations.Add("PRIMITIVE_UNAVAILABLE $msg")
        Write-GateVerdict -Violations $violations -Check:$true -Key 'EndpointKeyConsistency'
    }
    Write-Skip $msg
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
    if (-not [string]::IsNullOrEmpty((Get-HistoryEndpointId 'history.'))) { $nc.Add('history 空身份未被判出') }
    if (-not [string]::IsNullOrEmpty((Get-SessionEndpointId 'session.'))) { $nc.Add('session 空身份未被判出') }
    if (-not (Test-DuplicateWorkflowPrefix 'agent-round.agent-round.state_response')) { $nc.Add('node 重复 wf 未被判出') }
    if (-not (Test-DuplicateWorkflowPrefix 'agent-round.a.agent-round.a')) { $nc.Add('node 中段重复 wf 未被判出') }
    if (Test-DuplicateWorkflowPrefix 'agent-round.state_response@history:242e') { $nc.Add('node 合法端点被误判') }
    foreach ($m in $nc) { $violations.Add("[NC] $m") }
}

# ── 计数载体（集合 + 计数双判据；同名违规计数上涨即红）──
function Add-Count {
    param($Map, [string]$Key)
    if ([string]::IsNullOrWhiteSpace($Key)) { return }
    if ($Map.ContainsKey($Key)) { $Map[$Key] = [int]$Map[$Key] + 1 } else { $Map[$Key] = 1 }
}

function New-CountMap {
    return [hashtable]::new([System.StringComparer]::Ordinal)
}

# ── G1：history 源端点 ∈ 历史根派生端点集 ──
$rootEndpointIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($h in $histories) {
    if ($h.Doc -notmatch '^\[Record\]\s+history_root:') { continue }
    [void]$rootEndpointIds.Add((Get-HistoryEndpointId $h.Key))
}
$g1 = New-CountMap
$memberTaskKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($e in $edges) {
    $sid = Get-EndpointIdFromUniqueKey $e.SourceKey $e.SourceType
    $tid = Get-EndpointIdFromUniqueKey $e.TargetKey $e.TargetType
    if ($e.RefType -eq 'history_owns_task') {
        if ($e.TargetType -eq 'task') { [void]$memberTaskKeys.Add($tid) }
        if ($e.SourceType -eq 'history' -and -not $rootEndpointIds.Contains($sid)) { Add-Count $g1 $sid }
    }
}

# ── G2a/G2b：实体存在（∈ur_tasks）与树可达（∈history_owns_task.target）分离 ──
$g2a = New-CountMap
$g2b = New-CountMap
$taskSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($k in $taskKeys) { [void]$taskSet.Add($k) }
foreach ($e in $edges) {
    if ($e.RefType -ne 'cross_db_link' -or $e.SourceType -ne 'task') { continue }
    $sid = Get-EndpointIdFromUniqueKey $e.SourceKey $e.SourceType
    if (-not $taskSet.Contains($sid)) { Add-Count $g2a $sid }
    elseif (-not $memberTaskKeys.Contains($sid)) { Add-Count $g2b $sid }
}

# ── G3：端点 Id 不动点（history / session / node）──
$g3 = New-CountMap
$g4 = New-CountMap
foreach ($e in $edges) {
    foreach ($side in @(
        [pscustomobject]@{ Type = $e.SourceType; Key = $e.SourceKey },
        [pscustomobject]@{ Type = $e.TargetType; Key = $e.TargetKey })) {
        if ([string]::IsNullOrWhiteSpace($side.Type) -or [string]::IsNullOrWhiteSpace($side.Key)) { continue }
        $id = Get-EndpointIdFromUniqueKey $side.Key $side.Type
        switch ($side.Type) {
            'history' {
                if ((Get-HistoryEndpointId $id) -ne $id) { Add-Count $g3 $id }
                if ([string]::IsNullOrWhiteSpace((Get-HistoryIdentity $id))) { Add-Count $g3 $id }
            }
            'session' {
                if ((Get-SessionEndpointId $id) -ne $id) { Add-Count $g3 $id }
                if ([string]::IsNullOrWhiteSpace((Get-SessionIdentity $id))) { Add-Count $g3 $id }
            }
            'node' { if (Test-DuplicateWorkflowPrefix $id) { Add-Count $g3 $id } }
        }
    }
    if ($e.RefType -eq 'session_owns' -and $e.SourceType -eq 'session') {
        $sid = Get-EndpointIdFromUniqueKey $e.SourceKey $e.SourceType
        if ((Get-SessionEndpointId $sid) -ne $sid) { Add-Count $g4 $sid }
    }
}

# ── 基线棘轮：新键即红；同名计数上涨即红；基线中已消失的键提示收紧 ──
function Get-BaselineRows {
    param([string]$Name)
    $rows = [hashtable]::new([System.StringComparer]::Ordinal)
    $row = switch ($Name) {
        'G1'  { $baseline.G1 }
        'G2a' { $baseline.G2a }
        'G2b' { $baseline.G2b }
        'G3'  { $baseline.G3 }
        'G4'  { $baseline.G4 }
        default { $null }
    }
    if ($null -eq $row) { return $rows }
    foreach ($entry in @($row.Value)) {
        if ($entry -is [string]) { $rows[[string]$entry] = 1; continue }
        if ($entry -is [System.Collections.IDictionary]) {
            $key = [string]$entry['Key']
            if ([string]::IsNullOrWhiteSpace($key)) { continue }
            $count = if ($entry.Contains('Count')) { [int]$entry['Count'] } else { 1 }
            $rows[$key] = $count
        }
    }
    return $rows
}

function Add-Ratchet {
    param([string]$Name, $Current, [string]$BaselineName = '')
    if ([string]::IsNullOrWhiteSpace($BaselineName)) { $BaselineName = $Name }
    $allowed = Get-BaselineRows $BaselineName
    foreach ($k in $Current.Keys) {
        if (-not $allowed.ContainsKey($k)) {
            $violations.Add("[$Name] 未登记端点键：$k（新增违规 ⇒ 只降不升）")
        } elseif ([int]$Current[$k] -gt [int]$allowed[$k]) {
            $violations.Add("[$Name] 违规计数上涨：$k（actual=$($Current[$k]), baseline=$($allowed[$k])）")
        }
    }
    $disappeared = @($allowed.Keys | Where-Object { -not $Current.ContainsKey($_) })
    if ($disappeared.Count -gt 0) { $notes.Add("[$Name] 基线可收紧：已消失 $($disappeared.Count) 项（$($disappeared -join ', ')）") }
}

Add-Ratchet -Name 'G1' -Current $g1
Add-Ratchet -Name 'G2a' -Current $g2a
Add-Ratchet -Name 'G2b' -Current $g2b -BaselineName 'G2b'
Add-Ratchet -Name 'G3' -Current $g3
Add-Ratchet -Name 'G4' -Current $g4

foreach ($n in $notes) { Write-Host "  [INFO] $n" -ForegroundColor DarkGray }
Write-Host ("  [INFO] roots={0} member_tasks={1} g1={2} g2a={3} g2b={4} g3={5} g4={6}" -f `
    $rootEndpointIds.Count, $memberTaskKeys.Count, $g1.Count, $g2a.Count, $g2b.Count, $g3.Count, $g4.Count) -ForegroundColor DarkGray

Write-GateVerdict -Violations $violations -Check:$Check -Key 'EndpointKeyConsistency'

