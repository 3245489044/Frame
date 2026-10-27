#requires -Version 5.1
<#
.SYNOPSIS
    会话账本核对（B5c，只读）：每会话 smsg ≥ 1；输出空账会话清单（l-b-empty）。

.DESCRIPTION
    判据载体 = ur_nodes 中键族 smsg.{container} 的行（结构事实，单键单行）。
      - Sessions      = smsg 行数（每会话恰 1 行 ⇒ 行数 = 会话数）
      - Duplicates    = 同容器多行（B5 单键不成立，判红）
      - EmptySessions = 已知会话容器（ur_nodes 中其它域引用到的 container 不算；只统计 smsg 自身）
    说明：本脚本不推测「应有会话」（那需要业务事实），只判定已存在的 smsg 行是否满足单键约束；
    「会话有但 smsg 缺」由 `-ExpectContainers` 显式给定时才判红（禁静默猜默认，AX.NoSilentOutlet）。

    退出码：0 成功（或红判）/ 1 用法或取数失败 / 2 依赖缺失。

.PARAMETER StateRoot
    状态根（缺省 WF_DATA_ROOT → runtime.json → 仓库 Data）。

.PARAMETER DllDir
    DLL 目录（缺省自动探测；须含 System.Data.SQLite.dll）。

.PARAMETER ExpectContainers
    期望存在的会话容器清单（如从窗口/任务池导出）；缺 smsg 行 ⇒ 判红。

.PARAMETER Json
    JSON 输出。

.EXAMPLE
    pwsh Frame\PowerShellScript\Share\Data\Get-SessionLedgerAudit.ps1 -Json
#>
[CmdletBinding()]
param(
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [string[]]$ExpectContainers = @(),
    [string]$Table = 'ur_nodes',
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Fail { param([string]$Message) [Console]::Error.WriteLine("[SessionLedgerAudit] $Message") }

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
    Write-Fail '未找到 System.Data.SQLite.dll；请先构建 Frame 或用 -DllDir 指定（退出码 2）。'
    exit 2
}
$DllDir = (Resolve-Path -LiteralPath $DllDir).Path
$sqliteDll = Join-Path $DllDir 'System.Data.SQLite.dll'
if (-not (Test-Path -LiteralPath $sqliteDll)) { Write-Fail "缺依赖：$sqliteDll"; exit 2 }
try { Add-Type -Path $sqliteDll -ErrorAction Stop } catch { if ($_.Exception.Message -notmatch 'already') { throw } }

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
if (-not (Test-Path -LiteralPath $StateRoot)) { Write-Fail "状态根不存在：$StateRoot"; exit 1 }
$StateRoot = (Resolve-Path -LiteralPath $StateRoot).Path
$agentDb = Join-Path $StateRoot 'agent.db'
if (-not (Test-Path -LiteralPath $agentDb)) { Write-Fail "agent.db 不存在：$agentDb"; exit 1 }

$cn = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$agentDb;ReadOnly=True;Version=3;")
$cn.Open()
try {
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = "SELECT NodeKey FROM [$Table] WHERE NodeKey LIKE 'smsg.%' ORDER BY NodeKey"
    $reader = $cmd.ExecuteReader()
    $keys = New-Object System.Collections.Generic.List[string]
    while ($reader.Read()) { $keys.Add([string]$reader.GetString(0)) }
    $reader.Close()

    $duplicates = @($keys | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    $containers = @($keys | ForEach-Object { $_.Substring(5) } | Sort-Object -Unique)
    $missing = @()
    foreach ($expected in $ExpectContainers) {
        if ($containers -notcontains $expected) { $missing += $expected }
    }

    $red = ($duplicates.Count -gt 0) -or ($missing.Count -gt 0)
    $summary = [pscustomobject]@{
        Schema          = 'session-ledger-audit-v1'
        StateRoot       = $StateRoot
        Table           = $Table
        Sessions        = $containers.Count
        Rows            = $keys.Count
        Duplicates      = $duplicates
        MissingExpected = $missing
        Verdict         = if ($red) { 'fail' } else { 'pass' }
    }

    if ($Json) { $summary | ConvertTo-Json -Depth 6 }
    else {
        Write-Host ("session-ledger-audit sessions={0} rows={1} duplicates={2} missing={3} verdict={4}" -f `
            $summary.Sessions, $summary.Rows, $duplicates.Count, $missing.Count, $summary.Verdict)
        foreach ($d in $duplicates) { Write-Host "  [dup] $d" }
        foreach ($m in $missing) { Write-Host "  [missing] $m" }
    }

    exit 0
}
finally {
    $cn.Close()
}
