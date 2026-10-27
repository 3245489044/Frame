#requires -Version 5.1
<#
.SYNOPSIS
    任务身份/状态一致性审计（B0 取证，只读）：把「同一事实多载体」变成可复算清单。

.DESCRIPTION
    判据载体 = ur_tasks 行的 USharp 文档原文（结构事实）；本脚本只做**薄读取 + 逐字段对账**，
    不自建 DB 读取实现（取数唯一入口 = 同目录 Read-AgentDb.ps1）。

    解析口径（显式标注，不冒充反序列化）：
      method = usharp-line-scan —— 只读 UsharpRecordDecl.ToCanonical 产出的规范形态：
        · 顶层字段每行 `  name = value`（缩进 2 空格）
        · 嵌套 record 为单行 `{ k = v, ... }`
      形态不符（多行嵌套 / 无 [Record] 头 / 不可解析）⇒ 计入 ParseFailures，
      对应审计判 unknown（不得冒充 pass，AX.NoSilentOutlet）。

    两段审计：
      TaskKeyAudit  —— 任务实体行只允许 taskKey == nodeKey；LoopState.TaskKey / Data.task_id
                       必须为空或与 taskKey 同值；history: 不得进入 task 槽。
      TaskStateAudit —— envelope Status 为权威；Record.Status / LoopState.Status 若存在必须同值；
                       顶层重复 Status 行必须同值（envelope 与 body 不得分叉）。

    退出码：0 成功（含判 fail，诊断不产判决）/ 1 用法或取数失败 / 2 依赖缺失。

.PARAMETER TaskKey
    只审计指定任务键（缺省 = 全表，受 -Limit 约束）。

.PARAMETER StateRoot
    状态根（缺省由 Read-AgentDb 解析：WF_DATA_ROOT → runtime.json → 仓库 Data）。

.PARAMETER Limit
    最大扫描行数（缺省 1000；<=0 视为 1）。

.PARAMETER Json
    输出 JSON（机器可读）。

.PARAMETER IncludeRows
    JSON 输出附带逐行事实（默认只输出汇总与违例）。

.PARAMETER SelfTest
    正/负对照自检（合成文档，不读库）。

.EXAMPLE
    Frame\Invoke\Invoke-Frame.ps1 -Action task-consistency-audit -Json

.EXAMPLE
    pwsh Frame\PowerShellScript\Share\Data\Get-TaskConsistencyAudit.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [string]$TaskKey = '',
    [string]$StateRoot = '',
    [int]$Limit = 1000,
    [switch]$Json,
    [switch]$IncludeRows,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[TaskConsistencyAudit] $Message") }

# ── 1) USharp canonical 行扫描（只读；形态不符 ⇒ 显式不可解析）────────────────

function Split-TopLevel {
    param([string]$Text)

    $items = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($Text)) { return @() }

    $depth = 0
    $inString = $false
    $escaped = $false
    $start = 0
    for ($i = 0; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($inString) {
            if ($escaped) { $escaped = $false }
            elseif ($ch -eq '\') { $escaped = $true }
            elseif ($ch -eq '"') { $inString = $false }
            continue
        }

        if ($ch -eq '"') { $inString = $true; continue }
        if ($ch -eq '{' -or $ch -eq '[') { $depth++; continue }
        if ($ch -eq '}' -or $ch -eq ']') { $depth--; continue }
        if ($ch -eq ',' -and $depth -eq 0) {
            $items.Add($Text.Substring($start, $i - $start).Trim())
            $start = $i + 1
        }
    }

    $items.Add($Text.Substring($start).Trim())
    return $items.ToArray()
}

function ConvertTo-FieldText {
    param([string]$Raw)
    if ($null -eq $Raw) { return $null }
    $value = $Raw.Trim()
    if ($value.Length -ge 2 -and $value[0] -eq '"' -and $value[$value.Length - 1] -eq '"') {
        $inner = $value.Substring(1, $value.Length - 2)
        return $inner.Replace('\"', '"').Replace('\\', '\')
    }
    return $value
}

function Get-RecordField {
    param([string]$RecordText, [string]$Name)

    if ([string]::IsNullOrWhiteSpace($RecordText)) { return $null }
    foreach ($part in (Split-TopLevel $RecordText)) {
        $match = [regex]::Match($part, '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*([\s\S]*)$')
        if (-not $match.Success) { continue }
        if ([string]::Equals($match.Groups[1].Value, $Name, [System.StringComparison]::Ordinal)) {
            return $match.Groups[2].Value.Trim()
        }
    }
    return $null
}

function Get-BracedInner {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    $value = $Raw.Trim()
    if ($value.Length -ge 2 -and $value[0] -eq '{' -and $value[$value.Length - 1] -eq '}') {
        return $value.Substring(1, $value.Length - 2)
    }
    return $null
}

function Measure-Doc {
    param([string]$Doc)

    $result = [pscustomobject]@{
        Parseable      = $false
        Reason         = ''
        StatusValues   = @()
        TopTaskKey     = $null
        RecordTaskKey  = $null
        RecordStatus   = $null
        LoopTaskKey    = $null
        LoopStatus     = $null
        DataTaskId     = $null
        DataSessionKey = $null
        DataScopeKey   = $null
    }

    if ([string]::IsNullOrWhiteSpace($Doc)) {
        $result.Reason = 'doc-empty'
        return $result
    }

    $lines = $Doc -split "`r?`n"
    if ($lines.Count -eq 0 -or -not $lines[0].TrimStart().StartsWith('[Record]', [System.StringComparison]::Ordinal)) {
        $result.Reason = 'record-header-missing'
        return $result
    }

    $statusValues = New-Object System.Collections.Generic.List[string]
    $top = @{}
    foreach ($line in $lines) {
        $match = [regex]::Match($line, '^  ([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$')
        if (-not $match.Success) { continue }
        $name = $match.Groups[1].Value
        $raw = $match.Groups[2].Value.Trim()
        if ([string]::Equals($name, 'Status', [System.StringComparison]::Ordinal)) {
            $statusValues.Add((ConvertTo-FieldText $raw))
        }
        if (-not $top.ContainsKey($name)) { $top[$name] = $raw }
    }

    if ($top.Count -eq 0) {
        $result.Reason = 'record-body-unparsed'
        return $result
    }

    $recordInner = Get-BracedInner ($top['Record'])
    $loopInner = Get-BracedInner ($top['LoopState'])
    $dataInner = Get-BracedInner (Get-RecordField $loopInner 'Data')

    $result.StatusValues = $statusValues.ToArray()
    $result.TopTaskKey = ConvertTo-FieldText ($top['TaskKey'])
    $result.RecordTaskKey = ConvertTo-FieldText (Get-RecordField $recordInner 'TaskKey')
    $result.RecordStatus = ConvertTo-FieldText (Get-RecordField $recordInner 'Status')
    $result.LoopTaskKey = ConvertTo-FieldText (Get-RecordField $loopInner 'TaskKey')
    $result.LoopStatus = ConvertTo-FieldText (Get-RecordField $loopInner 'Status')
    $result.DataTaskId = ConvertTo-FieldText (Get-RecordField $dataInner 'task_id')
    $result.DataSessionKey = ConvertTo-FieldText (Get-RecordField $dataInner 'session_key')
    $result.DataScopeKey = ConvertTo-FieldText (Get-RecordField $dataInner 'scope_key')
    $result.Parseable = $true
    return $result
}


# ── 2) 逐行对账（纯函数；不写库）─────────────────────────────────────────────

function New-Violation {
    param([string]$Code, [string]$TaskKey, [string]$Field, [string]$Value, [string]$Detail)
    return [pscustomobject]@{
        Code    = $Code
        TaskKey = $TaskKey
        Field   = $Field
        Value   = $Value
        Detail  = $Detail
    }
}

function Test-IsHistoryKey {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return $Value.StartsWith('history:', [System.StringComparison]::Ordinal)
}

function Test-TaskRow {
    param([string]$NodeKey, $Fact)

    $keyViolations = New-Object System.Collections.Generic.List[object]
    $stateViolations = New-Object System.Collections.Generic.List[object]

    if (Test-IsHistoryKey $NodeKey) {
        $keyViolations.Add((New-Violation 'taskKey.history-in-task-slot' $NodeKey 'NodeKey' $NodeKey 'ur_tasks 行主键不得是 history: 实体'))
    }
    if (Test-IsHistoryKey $Fact.RecordTaskKey) {
        $keyViolations.Add((New-Violation 'taskKey.history-in-task-slot' $NodeKey 'Record.TaskKey' $Fact.RecordTaskKey 'Record.TaskKey 不得是 history: 实体'))
    }
    if (Test-IsHistoryKey $Fact.TopTaskKey) {
        $keyViolations.Add((New-Violation 'taskKey.history-in-task-slot' $NodeKey 'TaskKey' $Fact.TopTaskKey 'TaskKey 不得是 history: 实体'))
    }
    if (Test-IsHistoryKey $Fact.LoopTaskKey) {
        $keyViolations.Add((New-Violation 'taskKey.history-in-task-slot' $NodeKey 'LoopState.TaskKey' $Fact.LoopTaskKey 'history: 不得进入 LoopState.TaskKey'))
    }
    if (Test-IsHistoryKey $Fact.DataTaskId) {
        $keyViolations.Add((New-Violation 'taskKey.history-in-task-slot' $NodeKey 'Data.task_id' $Fact.DataTaskId 'history: 不得进入 Data.task_id'))
    }

    foreach ($pair in @(
            @{ Field = 'Record.TaskKey'; Value = $Fact.RecordTaskKey },
            @{ Field = 'TaskKey'; Value = $Fact.TopTaskKey },
            @{ Field = 'LoopState.TaskKey'; Value = $Fact.LoopTaskKey },
            @{ Field = 'Data.task_id'; Value = $Fact.DataTaskId })) {
        $value = [string]$pair.Value
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not [string]::Equals($value, $NodeKey, [System.StringComparison]::Ordinal)) {
            $keyViolations.Add((New-Violation 'taskKey.diverged' $NodeKey $pair.Field $value "期望与 NodeKey 同值：$NodeKey"))
        }
    }

    $statuses = @($Fact.StatusValues)
    $envelopeStatus = if ($statuses.Count -gt 0) { [string]$statuses[0] } else { '' }
    if ([string]::IsNullOrWhiteSpace($envelopeStatus)) {
        $stateViolations.Add((New-Violation 'status.missing' $NodeKey 'Status' '' 'envelope Status 缺失（不可判定）'))
    }

    foreach ($dup in $statuses) {
        $value = [string]$dup
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not [string]::Equals($value, $envelopeStatus, [System.StringComparison]::Ordinal)) {
            $stateViolations.Add((New-Violation 'status.duplicate-diverged' $NodeKey 'Status' $value "顶层重复 Status 与 envelope 不同值：$envelopeStatus"))
        }
    }

    foreach ($pair in @(
            @{ Field = 'Record.Status'; Value = $Fact.RecordStatus },
            @{ Field = 'LoopState.Status'; Value = $Fact.LoopStatus })) {
        $value = [string]$pair.Value
        if ([string]::IsNullOrWhiteSpace($value)) { continue }
        if (-not [string]::IsNullOrWhiteSpace($envelopeStatus) -and
            -not [string]::Equals($value, $envelopeStatus, [System.StringComparison]::Ordinal)) {
            $stateViolations.Add((New-Violation 'status.body-diverged' $NodeKey $pair.Field $value "与 envelope Status 不同值：$envelopeStatus"))
        }
    }

    return [pscustomobject]@{
        TaskKey         = $NodeKey
        KeyViolations   = $keyViolations.ToArray()
        StateViolations = $stateViolations.ToArray()
        Fact            = $Fact
    }
}

function Get-Verdict {
    param([int]$ViolationCount, [int]$UnknownCount, [int]$Scanned)
    if ($Scanned -eq 0) { return 'unknown' }
    if ($ViolationCount -gt 0) { return 'fail' }
    if ($UnknownCount -gt 0) { return 'unknown' }
    return 'pass'
}


# ── 3) 自检（正/负对照；不读库）─────────────────────────────────────────────

function Invoke-SelfTest {
    $cases = New-Object System.Collections.Generic.List[object]

    $clean = @'
[Record] task:clean: table=Tasks | key=clean
  ThreadId = "taskpool"
  Status = "Completed"
  Record = { TaskKey = "clean", Status = "Completed" }
  LoopState = { TaskKey = "clean", Status = "Completed", Data = { task_id = "clean", session_key = "history:h1" } }
  TaskKey = "clean"
'@

    $divergent = @'
[Record] task:clean: table=Tasks | key=clean
  ThreadId = "taskpool"
  Status = "Completed"
  Record = { TaskKey = "clean", Status = "NotStarted" }
  LoopState = { TaskKey = "9c3f", Status = "NotStarted", Data = { task_id = "history:h1", session_key = "history:h1" } }
  TaskKey = "clean"
'@

    $unparsed = 'not-a-usharp-record'

    $cleanFact = Measure-Doc $clean
    $cleanRow = Test-TaskRow -NodeKey 'clean' -Fact $cleanFact
    $cases.Add([pscustomobject]@{
            Case            = 'positive-clean'
            Expected        = '0/0'
            KeyViolations   = @($cleanRow.KeyViolations).Count
            StateViolations = @($cleanRow.StateViolations).Count
            Passed          = ($cleanFact.Parseable -and @($cleanRow.KeyViolations).Count -eq 0 -and @($cleanRow.StateViolations).Count -eq 0)
        })

    $divFact = Measure-Doc $divergent
    $divRow = Test-TaskRow -NodeKey 'clean' -Fact $divFact
    $cases.Add([pscustomobject]@{
            Case            = 'negative-divergent'
            Expected        = '>0/>0'
            KeyViolations   = @($divRow.KeyViolations).Count
            StateViolations = @($divRow.StateViolations).Count
            Passed          = ($divFact.Parseable -and @($divRow.KeyViolations).Count -gt 0 -and @($divRow.StateViolations).Count -gt 0)
        })

    $badFact = Measure-Doc $unparsed
    $cases.Add([pscustomobject]@{
            Case            = 'negative-unparsed'
            Expected        = 'Parseable=false'
            KeyViolations   = 0
            StateViolations = 0
            Passed          = (-not $badFact.Parseable)
        })

    $failed = @($cases.ToArray() | Where-Object { -not $_.Passed })
    $passed = ($failed.Count -eq 0)
    return [pscustomobject]@{
        Schema = 'task-consistency-audit-selftest-v1'
        Passed = $passed
        Cases  = $cases.ToArray()
    }
}

if ($SelfTest) {
    $self = Invoke-SelfTest
    if ($Json) { $self | ConvertTo-Json -Depth 6 } else { $self | Format-List; $self.Cases | Format-Table -AutoSize }
    if ($self.Passed) { exit 0 }
    Write-Fail '自检失败：正/负对照未按预期表现'
    exit 1
}


# ── 4) 取数与汇总（只经 Read-AgentDb）──────────────────────────────────────────

$reader = Join-Path $PSScriptRoot 'Read-AgentDb.ps1'
if (-not (Test-Path -LiteralPath $reader)) {
    Write-Fail "取数入口缺失：$reader（禁止在本脚本内自建 DB 读取实现）"
    exit 2
}

$where = ''
if (-not [string]::IsNullOrWhiteSpace($TaskKey)) {
    $where = "NodeKey = '" + $TaskKey.Replace("'", "''") + "'"
}

$callArgs = @{ Store = 'agent'; Table = 'ur_tasks'; Json = $true; Limit = [Math]::Max(1, $Limit) }
if (-not [string]::IsNullOrWhiteSpace($where)) { $callArgs['Where'] = $where }
if (-not [string]::IsNullOrWhiteSpace($StateRoot)) { $callArgs['StateRoot'] = $StateRoot }

try {
    $raw = & $reader @callArgs
} catch {
    Write-Fail $_.Exception.Message
    exit 1
}

$entry = $null
foreach ($item in @($raw | ConvertFrom-Json)) {
    if ($null -ne $item -and $item.PSObject.Properties['Table'] -and [string]$item.Table -eq 'ur_tasks') { $entry = $item; break }
}

if ($null -eq $entry) {
    $payload = [pscustomobject]@{
        Schema         = 'task-consistency-audit-v1'
        Method         = 'usharp-line-scan'
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        StateRoot      = $StateRoot
        Table          = 'ur_tasks'
        Filter         = $where
        Scanned        = 0
        ParseFailures  = 0
        TaskKeyAudit   = [pscustomobject]@{ Verdict = 'unknown'; ViolationCount = 0; Violations = @() }
        TaskStateAudit = [pscustomobject]@{ Verdict = 'unknown'; ViolationCount = 0; Violations = @() }
        Reason         = 'table-or-entry-missing'
    }
    if ($Json) { $payload | ConvertTo-Json -Depth 8 } else { $payload | Format-List }
    exit 0
}

$rows = @()
if ($entry.PSObject.Properties['Rows'] -and $null -ne $entry.Rows) { $rows = @($entry.Rows) }

$keyViolations = New-Object System.Collections.Generic.List[object]
$stateViolations = New-Object System.Collections.Generic.List[object]
$rowFacts = New-Object System.Collections.Generic.List[object]
$parseFailures = 0

foreach ($row in $rows) {
    $nodeKey = ''
    if ($row.PSObject.Properties['NodeKey']) { $nodeKey = [string]$row.NodeKey }
    $doc = ''
    if ($row.PSObject.Properties['Doc']) { $doc = [string]$row.Doc }

    $fact = Measure-Doc $doc
    if (-not $fact.Parseable) {
        $parseFailures++
        $rowFacts.Add([pscustomobject]@{ TaskKey = $nodeKey; Parseable = $false; Reason = $fact.Reason })
        continue
    }

    $audit = Test-TaskRow -NodeKey $nodeKey -Fact $fact
    foreach ($v in @($audit.KeyViolations)) { $keyViolations.Add($v) }
    foreach ($v in @($audit.StateViolations)) { $stateViolations.Add($v) }
    $statuses = @($fact.StatusValues)
    $rowFacts.Add([pscustomobject]@{
            TaskKey         = $nodeKey
            Parseable       = $true
            EnvelopeStatus  = if ($statuses.Count -gt 0) { [string]$statuses[0] } else { '' }
            RecordTaskKey   = $fact.RecordTaskKey
            RecordStatus    = $fact.RecordStatus
            LoopTaskKey     = $fact.LoopTaskKey
            LoopStatus      = $fact.LoopStatus
            DataTaskId      = $fact.DataTaskId
            DataSessionKey  = $fact.DataSessionKey
            DataScopeKey    = $fact.DataScopeKey
            KeyViolations   = @($audit.KeyViolations).Count
            StateViolations = @($audit.StateViolations).Count
        })
}


$keyVerdict = Get-Verdict -ViolationCount $keyViolations.Count -UnknownCount $parseFailures -Scanned $rows.Count
$stateVerdict = Get-Verdict -ViolationCount $stateViolations.Count -UnknownCount $parseFailures -Scanned $rows.Count

$payload = [pscustomobject]@{
    Schema         = 'task-consistency-audit-v1'
    Method         = 'usharp-line-scan'
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    StateRoot      = $StateRoot
    Table          = 'ur_tasks'
    Filter         = $where
    Scanned        = $rows.Count
    ParseFailures  = $parseFailures
    TaskKeyAudit   = [pscustomobject]@{
        Verdict        = $keyVerdict
        ViolationCount = $keyViolations.Count
        Violations     = $keyViolations.ToArray()
    }
    TaskStateAudit = [pscustomobject]@{
        Verdict        = $stateVerdict
        ViolationCount = $stateViolations.Count
        Violations     = $stateViolations.ToArray()
    }
}

if ($IncludeRows) {
    $payload | Add-Member -NotePropertyName Rows -NotePropertyValue $rowFacts.ToArray()
}

if ($Json) {
    $payload | ConvertTo-Json -Depth 10
} else {
    Write-Host ("task-consistency-audit scanned={0} parseFailures={1}" -f $payload.Scanned, $payload.ParseFailures)
    Write-Host ("  TaskKeyAudit   = {0} (violations={1})" -f $payload.TaskKeyAudit.Verdict, $payload.TaskKeyAudit.ViolationCount)
    Write-Host ("  TaskStateAudit = {0} (violations={1})" -f $payload.TaskStateAudit.Verdict, $payload.TaskStateAudit.ViolationCount)
    foreach ($v in @($payload.TaskKeyAudit.Violations)) { Write-Host ("  [key]   {0} {1}: {2} ({3})" -f $v.Code, $v.Field, $v.Value, $v.Detail) }
    foreach ($v in @($payload.TaskStateAudit.Violations)) { Write-Host ("  [state] {0} {1}: {2} ({3})" -f $v.Code, $v.Field, $v.Value, $v.Detail) }
}

exit 0

