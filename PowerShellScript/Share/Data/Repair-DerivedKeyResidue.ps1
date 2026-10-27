<#
.SYNOPSIS
    派生键残留归位（P0-4 / B 批）：可见化并标记 orphan —— 不改 NodeKey、不删行、可重复执行。
.DESCRIPTION
    治「哈希派生前缀被当作业务键落库」：`task_member_<hash>` 不可逆 ⇒ **禁止回填业务键**，只标 orphan。
    纪律：
      - 缺省 = -DryRun（0 写，只出将变更的行清单）；-Apply 才写。
      - -Apply 先备份 `agent.db`（`*.bak-<UTC>`），写前二次校验行数，写后复核行数不增删。
      - 幂等：`status_note` 已是目标值 ⇒ 不再更新（二次执行 0 变更）。
      - 表无 `status_note` 列 ⇒ 显式 Unavailable（不擅自改 schema）；表不存在 ⇒ 显式 Unavailable。
      - `-IncludeGraph` 走 LiteDB `cross_db_link`：schema 未登记 ⇒ 显式 Unavailable（不猜字段、不静默）。
    退出码：0 成功 / 1 库缺失·用法错·对账失败 / 2 依赖 DLL 缺失或未知异常。
.EXAMPLE
    pwsh Frame\PowerShellScript\Share\Data\Repair-DerivedKeyResidue.ps1 -DryRun
.EXAMPLE
    pwsh Frame\PowerShellScript\Share\Data\Repair-DerivedKeyResidue.ps1 -Apply -Json
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [switch]$DryRun,
    [switch]$Apply,
    [switch]$IncludeGraph,
    [string]$Marker = 'orphan-member',
    [string]$Pattern = 'task_member_%',
    [string[]]$Tables = @('ur_tasks', 'ur_histories'),
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[Repair-DerivedKeyResidue] $Message") }

if ($DryRun -and $Apply) { Write-Fail '-DryRun 与 -Apply 互斥（缺省即 DryRun）。'; exit 1 }
if (-not $Apply) { $DryRun = $true }
if ($Marker -notmatch '^[A-Za-z0-9_-]{1,32}$') { Write-Fail "-Marker 只允许字母/数字/下划线/连字符：$Marker"; exit 1 }

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($DllDir)) {
    $DllDir = @(
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Debug'),
        (Join-Path $repoRoot 'Projects\FileDriven\BuildOutput\Release')
    ) | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'System.Data.SQLite.dll') } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($DllDir) -or -not (Test-Path -LiteralPath $DllDir)) {
    Write-Fail '未找到 System.Data.SQLite.dll；请先构建 FileDriven 或用 -DllDir 指定（缺依赖 ⇒ 退出码 2）。'
    exit 2
}
$DllDir = (Resolve-Path -LiteralPath $DllDir).Path
$sqliteDll = Join-Path $DllDir 'System.Data.SQLite.dll'
if (-not (Test-Path -LiteralPath $sqliteDll)) { Write-Fail "缺依赖：$sqliteDll"; exit 2 }

function Import-Assembly {
    param([string]$Path)
    try { Add-Type -Path $Path -ErrorAction Stop }
    catch { if ($_.Exception.Message -notmatch 'already') { throw } }
}

# 状态根解析（对齐 Read-AgentDb / AgentStateLocator）：环境变量 → runtime.json → 仓库 Data。
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
if (-not (Test-Path -LiteralPath $agentDb)) { Write-Fail "agent.db 不存在：$agentDb"; exit 1 }

Import-Assembly $sqliteDll

function Open-Connection {
    param([string]$DbPath, [bool]$ReadOnly)
    $mode = if ($ReadOnly) { 'ReadOnly=True;' } else { '' }
    $cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DbPath;$mode Version=3;")
    $cn.Open()
    return $cn
}

function Get-TableColumns {
    param($Connection, [string]$Table)
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = "PRAGMA table_info([$Table])"
    $reader = $cmd.ExecuteReader()
    $columns = @()
    while ($reader.Read()) { $columns += [string]$reader.GetString(1) }
    $reader.Close()
    return $columns
}

function Get-ResidueRows {
    param($Connection, [string]$Table, [int]$Limit)
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = "SELECT NodeKey FROM [$Table] WHERE NodeKey LIKE @pattern ORDER BY NodeKey"
    $null = $cmd.Parameters.AddWithValue('@pattern', $Pattern)
    $reader = $cmd.ExecuteReader()
    $rows = @()
    while ($reader.Read()) {
        if ($rows.Count -ge $Limit) { break }
        $rows += [string]$reader.GetString(0)
    }
    $reader.Close()
    return $rows
}

function Get-ResidueCount {
    param($Connection, [string]$Table)
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM [$Table] WHERE NodeKey LIKE @pattern"
    $null = $cmd.Parameters.AddWithValue('@pattern', $Pattern)
    return [int64]$cmd.ExecuteScalar()
}

function Get-RowCount {
    param($Connection, [string]$Table)
    $cmd = $Connection.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM [$Table]"
    return [int64]$cmd.ExecuteScalar()
}

$report = [System.Collections.Generic.List[object]]::new()
$failed = $false

try {
    if ($Apply) {
        $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
        $agentBackup = "$agentDb.bak-$stamp"
        Copy-Item -LiteralPath $agentDb -Destination $agentBackup -Force
        $report.Add([pscustomobject]@{ Store = 'agent'; Table = '*'; Action = 'Backup'; Key = (Split-Path -Leaf $agentBackup); Count = 1; Status = 'Ok' })
    }

    $cn = Open-Connection -DbPath $agentDb -ReadOnly:(-not $Apply)
    try {
        $tables = @($Tables | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        foreach ($table in $tables) {
            $columns = @(Get-TableColumns -Connection $cn -Table $table)
            if ($columns.Count -eq 0) {
                $report.Add([pscustomobject]@{ Store = 'agent'; Table = $table; Action = 'Skipped'; Key = ''; Count = 0; Status = 'Unavailable:table-missing' })
                continue
            }

            $residue = Get-ResidueCount -Connection $cn -Table $table
            if ($residue -eq 0) {
                $report.Add([pscustomobject]@{ Store = 'agent'; Table = $table; Action = 'NoOp'; Key = ''; Count = 0; Status = 'Ok' })
                continue
            }

            $rows = @(Get-ResidueRows -Connection $cn -Table $table -Limit 20)
            if ($columns -notcontains 'status_note') {
                # 显式 Unavailable：不擅自改 schema（只报事实与将处理的行数）。
                $report.Add([pscustomobject]@{ Store = 'agent'; Table = $table; Action = 'Unavailable'; Key = ($rows -join ';'); Count = $residue; Status = 'Unavailable:status_note-column-missing' })
                continue
            }

            if ($DryRun) {
                $report.Add([pscustomobject]@{ Store = 'agent'; Table = $table; Action = 'MarkOrphan'; Key = ($rows -join ';'); Count = $residue; Status = 'DryRun' })
                continue
            }

            $before = Get-RowCount -Connection $cn -Table $table
            $cmd = $cn.CreateCommand()
            $cmd.CommandText = "UPDATE [$table] SET status_note = @marker WHERE NodeKey LIKE @pattern AND (status_note IS NULL OR status_note <> @marker)"
            $null = $cmd.Parameters.AddWithValue('@marker', $Marker)
            $null = $cmd.Parameters.AddWithValue('@pattern', $Pattern)
            $affected = [int]$cmd.ExecuteNonQuery()
            $after = Get-RowCount -Connection $cn -Table $table
            $status = if ($before -eq $after) { 'Ok' } else { 'Failed:row-count-changed' }
            if ($status -ne 'Ok') { $failed = $true }
            $report.Add([pscustomobject]@{ Store = 'agent'; Table = $table; Action = 'MarkOrphan'; Key = ($rows -join ';'); Count = $affected; Status = $status })
        }
    }
    finally {
        $cn.Close()
        $cn.Dispose()
    }

    if ($IncludeGraph) {
        if (-not (Test-Path -LiteralPath $graphDb)) {
            $report.Add([pscustomobject]@{ Store = 'graph'; Table = 'cross_db_link'; Action = 'Skipped'; Key = ''; Count = 0; Status = 'Unavailable:db-missing' })
        }
        else {
            # schema 未登记 ⇒ 只报 Unavailable（不猜字段、不静默改写）。
            $report.Add([pscustomobject]@{ Store = 'graph'; Table = 'cross_db_link'; Action = 'Unavailable'; Key = ''; Count = 0; Status = 'Unavailable:schema-not-registered' })
        }
    }
}
catch {
    Write-Fail $_.Exception.Message
    exit 2
}

$mode = if ($Apply) { 'Apply' } else { 'DryRun' }
if ($Json) {
    [pscustomobject]@{
        Mode = $mode
        StateRoot = $StateRoot
        Pattern = $Pattern
        Marker = $Marker
        Rows = $report
        Total = [int64](($report | Measure-Object -Property Count -Sum).Sum)
    } | ConvertTo-Json -Depth 6
}
else {
    $report | Format-Table -AutoSize | Out-String | Write-Output
    Write-Output ("[Repair-DerivedKeyResidue] mode={0} stateRoot={1} pattern={2} marker={3}" -f $mode, $StateRoot, $Pattern, $Marker)
}

if ($failed) { exit 1 }
exit 0

