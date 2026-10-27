#requires -Version 5.1
<#
.SYNOPSIS
    任务快照残留归位（O1，受控写）：清除 ur_tasks 文档内 Record/LoopState 的 TaskKey/Status 残留。

.DESCRIPTION
    B3a/B3b 后，任务状态唯一持久面 = 记录**顶层** Status（envelope 字段，由 store 注入）；
    payload 内 Record.TaskKey/Record.Status/LoopState.TaskKey/LoopState.Status 属历史残留
    （读取已自愈，但审计原始磁盘面仍非 0）。本脚本只清残留字段，不改状态值、不改 NodeKey。

    纪律:
      - 缺省 = -DryRun（0 写，只出将变更的行清单）；-Apply 才写。
      - 读写**只经 UsharpDocumentCodec**（反序列化 → 移除残留 → 规范重写）；禁止正则改文本。
      - -Apply 先备份 agent.db（*.bak-<UTC>），写前/写后行数守恒；__createdAt 保留（非本脚本字段）。
      - 幂等：已无残留 ⇒ 0 变更（二次执行 changed=0）。
      - 依赖缺失（SQLite/Usharp DLL）⇒ 退出码 2（不静默降级）。
    退出码：0 成功 / 1 用法或对账失败 / 2 依赖缺失。

.PARAMETER StateRoot
    状态根（缺省 WF_DATA_ROOT → runtime.json → 仓库 Data）。

.PARAMETER DllDir
    DLL 目录（缺省自动探测 BuildOutput\Debug|Release；须含 System.Data.SQLite.dll + Usharp DLL 族）。

.PARAMETER DryRun
    只出清单（缺省即此模式）。

.PARAMETER Apply
    执行写入（与 -DryRun 互斥）。

.PARAMETER Json
    JSON 输出。

.PARAMETER Limit
    最大扫描行数（缺省 1000）。

.EXAMPLE
    pwsh Frame\PowerShellScript\Share\Data\Repair-TaskSnapshotResidue.ps1 -DryRun -Json

.EXAMPLE
    pwsh Frame\PowerShellScript\Share\Data\Repair-TaskSnapshotResidue.ps1 -Apply
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [string]$UsharpDir = '',
    [switch]$DryRun,
    [switch]$Apply,
    [string]$Table = 'ur_tasks',
    [int]$Limit = 1000,
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[Repair-TaskSnapshotResidue] $Message") }

if ($DryRun -and $Apply) { Write-Fail '-DryRun 与 -Apply 互斥（缺省即 DryRun）。'; exit 1 }
if (-not $Apply) { $DryRun = $true }
if ($Limit -le 0) { $Limit = 1 }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path

if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = @(
        (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Frame\CsharpPrj\BuildOutput\Release'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'System.Data.SQLite.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($DllDir) -or -not (Test-Path -LiteralPath $DllDir)) {
    Write-Fail '未找到 System.Data.SQLite.dll；请先构建 Frame/FileDriven 或用 -DllDir 指定（退出码 2）。'
    exit 2
}
$DllDir = (Resolve-Path -LiteralPath $DllDir).Path

# USharp DLL 族（codec 依赖）与 SQLite 分属不同构建输出：Frame\Usharp\BuildOutput\<cfg>。
if ([string]::IsNullOrWhiteSpace($UsharpDir)) {
    $UsharpDir = @(
        (Join-Path $repoRoot 'Frame\Usharp\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Frame\Usharp\BuildOutput\Release')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'Usharp.Adapters.Declarations.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($UsharpDir) -or -not (Test-Path -LiteralPath $UsharpDir)) {
    Write-Fail '未找到 Usharp.Adapters.Declarations.dll；请先构建 Frame\Usharp 或用 -UsharpDir 指定（退出码 2）。'
    exit 2
}
$UsharpDir = (Resolve-Path -LiteralPath $UsharpDir).Path

foreach ($dll in @('System.Data.SQLite.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $DllDir $dll))) {
        Write-Fail "缺依赖：$(Join-Path $DllDir $dll)（退出码 2）"
        exit 2
    }
}
foreach ($dll in @('Usharp.Adapters.Declarations.dll', 'Usharp.Syntax.dll', 'Plate.DslMethod.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $UsharpDir $dll))) {
        Write-Fail "缺依赖：$(Join-Path $UsharpDir $dll)（退出码 2）"
        exit 2
    }
}

function Import-Assembly {
    param([string]$Path)
    try { Add-Type -Path $Path -ErrorAction Stop }
    catch { if ($_.Exception.Message -notmatch 'already') { throw } }
}

foreach ($name in @('Plate.DslMethod.dll', 'Usharp.Syntax.dll', 'Usharp.Adapters.Declarations.dll')) {
    Import-Assembly (Join-Path $UsharpDir $name)
}
Import-Assembly (Join-Path $DllDir 'System.Data.SQLite.dll')

if ([string]::IsNullOrWhiteSpace($StateRoot)) {
    if (-not [string]::IsNullOrWhiteSpace($env:WF_DATA_ROOT)) { $StateRoot = $env:WF_DATA_ROOT }
    elseif (-not [string]::IsNullOrWhiteSpace($env:FILEDRIVEN_DATA_ROOT)) { $StateRoot = $env:FILEDRIVEN_DATA_ROOT }
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
if (-not (Test-Path -LiteralPath $agentDb)) { Write-Fail "agent.db 不存在：$agentDb"; exit 1 }

# 残留字段（唯一清单；payload 内嵌套 record 下需移除的字段）
$ResidueFields = @{
    'Record'    = @('TaskKey', 'Status')
    'LoopState' = @('TaskKey', 'Status')
}

function New-Kv { param([string]$Key, $Value) [System.Collections.Generic.KeyValuePair[string, Plate.DslMethod.Usharp.UsharpValue]]::new($Key, $Value) }

# 规范化：移除嵌套残留字段；Changed=false ⇒ 无需写回（幂等）。
function Remove-Residue {
    param([string]$Doc, [string]$NodeKey)

    $recordId = $null; $tableId = $null; $keyId = $null; $body = $null
    $codec = [Usharp.Adapters.Declarations.UsharpDocumentCodec]::Instance
    if (-not $codec.TryDeserializeRecord($Doc, [ref]$recordId, [ref]$tableId, [ref]$keyId, [ref]$body)) {
        return [pscustomobject]@{ NodeKey = $NodeKey; Changed = $false; Unparsed = $true; Removed = @(); Text = $null }
    }

    $removed = New-Object System.Collections.Generic.List[string]
    $top = [System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string, Plate.DslMethod.Usharp.UsharpValue]]]::new()
    foreach ($field in $body.Fields) {
        if ($ResidueFields.ContainsKey($field.Key) -and $field.Value -is [Plate.DslMethod.Usharp.UsharpRecord]) {
            $nested = [Plate.DslMethod.Usharp.UsharpRecord]$field.Value
            $kept = [System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string, Plate.DslMethod.Usharp.UsharpValue]]]::new()
            foreach ($inner in $nested.Fields) {
                if ($ResidueFields[$field.Key] -contains $inner.Key) {
                    $removed.Add("$($field.Key).$($inner.Key)")
                    continue
                }
                $kept.Add((New-Kv -Key $inner.Key -Value $inner.Value))
            }
            $top.Add((New-Kv -Key $field.Key -Value ([Plate.DslMethod.Usharp.UsharpRecord]::new($kept))))
            continue
        }
        $top.Add((New-Kv -Key $field.Key -Value $field.Value))
    }

    if ($removed.Count -eq 0) {
        return [pscustomobject]@{ NodeKey = $NodeKey; Changed = $false; Unparsed = $false; Removed = @(); Text = $null }
    }

    $text = $codec.SerializeRecord($recordId, $tableId, $keyId, [Plate.DslMethod.Usharp.UsharpRecord]::new($top))
    return [pscustomobject]@{ NodeKey = $NodeKey; Changed = $true; Unparsed = $false; Removed = $removed.ToArray(); Text = $text }
}

function Open-Connection {
    param([string]$DbPath, [bool]$ReadOnly)
    $mode = if ($ReadOnly) { 'ReadOnly=True;' } else { '' }
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;$mode Version=3;")
    $cn.Open()
    return $cn
}

$cn = Open-Connection -DbPath $agentDb -ReadOnly (-not $Apply)
try {
    $countCmd = $cn.CreateCommand()
    $countCmd.CommandText = "SELECT COUNT(*) FROM [$Table]"
    $before = [int]$countCmd.ExecuteScalar()

    $select = $cn.CreateCommand()
    $select.CommandText = "SELECT NodeKey, Doc FROM [$Table] ORDER BY NodeKey LIMIT @limit"
    $null = $select.Parameters.AddWithValue('@limit', $Limit)
    $reader = $select.ExecuteReader()
    $rows = New-Object System.Collections.Generic.List[object]
    while ($reader.Read()) {
        $rows.Add([pscustomobject]@{ NodeKey = [string]$reader.GetString(0); Doc = [string]$reader.GetString(1) })
    }
    $reader.Close()

    $changes = New-Object System.Collections.Generic.List[object]
    $unparsed = 0
    foreach ($row in $rows) {
        $result = Remove-Residue -Doc $row.Doc -NodeKey $row.NodeKey
        if ($result.Unparsed) { $unparsed++ }
        if ($result.Changed) {
            $changes.Add([pscustomobject]@{ NodeKey = $result.NodeKey; Removed = $result.Removed; Text = $result.Text })
        }
    }

    $backup = $null
    $after = $before
    if ($Apply -and $changes.Count -gt 0) {
        $backup = "$agentDb.bak-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
        Copy-Item -LiteralPath $agentDb -Destination $backup -Force
        foreach ($change in $changes) {
            $update = $cn.CreateCommand()
            $update.CommandText = "UPDATE [$Table] SET Doc=@d WHERE NodeKey=@k"
            $null = $update.Parameters.AddWithValue('@d', $change.Text)
            $null = $update.Parameters.AddWithValue('@k', $change.NodeKey)
            $null = $update.ExecuteNonQuery()
        }
        $after = [int]$countCmd.ExecuteScalar()
        if ($after -ne $before) {
            Write-Fail "行数不守恒：before=$before after=$after（已备份 $backup）"
            exit 1
        }
    }

    $summary = [pscustomobject]@{
        Schema      = 'task-snapshot-residue-v1'
        Method      = 'usharp-document-codec'
        Mode        = if ($Apply) { 'apply' } else { 'dry-run' }
        StateRoot   = $StateRoot
        Table       = $Table
        Scanned     = $rows.Count
        ChangedRows = $changes.Count
        Unparsed    = $unparsed
        RowsBefore  = $before
        RowsAfter   = $after
        Backup      = $backup
        Changes     = $changes.ToArray()
    }

    if ($Json) { $summary | ConvertTo-Json -Depth 8 }
    else {
        Write-Host ("task-snapshot-residue mode={0} scanned={1} changed={2} unparsed={3} rows={4}->{5}" -f `
            $summary.Mode, $summary.Scanned, $summary.ChangedRows, $summary.Unparsed, $summary.RowsBefore, $summary.RowsAfter)
        foreach ($c in $summary.Changes) { Write-Host ("  {0}: {1}" -f $c.NodeKey, ($c.Removed -join ',')) }
    }

    if ($unparsed -gt 0) { exit 1 }
    exit 0
}
finally {
    $cn.Close()
}
