<#
.SYNOPSIS
    任务种子审计（B0 取证，只读）：导出「簿记输入」与「数据空间种子」的差异。
.DESCRIPTION
    判据载体 = 库导出原文（结构事实），本脚本只做薄封装 + 键集存在性计算，不自建 DB 读取实现。
    取数唯一入口 = 同目录 Read-AgentDb.ps1（-Store agent -Table ... -Json）。

    输出字段：
      Record.UserInputPresent  簿记字段是否非空
      Data.UserInputPresent    数据空间是否含 user_input
      History.TextCount        会话消息条数（原始 JSON 计数）
      NodeCount                节点行数
      Verdict                  seed-ok | seed-missing | seed-empty-input
    方法字段 Method = raw-regex（显式标注：不做 schema 反序列化，避免对未知载荷形状的脆弱假设）。
.EXAMPLE
    .\Get-TaskSeedAudit.ps1 -TaskKey task_member_25aacaa2ebd20d2a
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TaskKey,

    [string]$StateRoot = '',

    [switch]$Json
)

$ErrorActionPreference = 'Stop'
$reader = Join-Path $PSScriptRoot 'Read-AgentDb.ps1'
if (-not (Test-Path -LiteralPath $reader)) {
    throw "取数入口缺失：$reader（禁止在本脚本内自建 DB 读取实现）"
}

function Read-Raw {
    param([string]$Table, [string]$Where)
    $callArgs = @{ Store = 'agent'; Table = $Table; Where = $Where; Json = $true }
    if (-not [string]::IsNullOrWhiteSpace($StateRoot)) { $callArgs['StateRoot'] = $StateRoot }
    $raw = & $reader @callArgs 2>$null
    if ($null -eq $raw) { return '' }
    return (@($raw) -join "`n")
}

$where = "NodeKey = '$TaskKey'"
$taskRaw = Read-Raw -Table 'ur_tasks' -Where $where
$historyRaw = Read-Raw -Table 'ur_histories' -Where $where
$nodeRaw = Read-Raw -Table 'ur_nodes' -Where "NodeKey LIKE '%$TaskKey%'"

$recordInput = $taskRaw -match 'UserInput\s*=\s*\\*"(?<v>[^"\\]+)\\*"'
$dataInput = $taskRaw -match 'user_input\s*=\s*\\*"(?<v>[^"\\]+)\\*"'
$textCount = ([regex]::Matches($historyRaw, 'Text\s*=')).Count
$nodeCount = ([regex]::Matches($nodeRaw, '"NodeKey"\s*:')).Count

$verdict = if ($recordInput -and $dataInput) { 'seed-ok' }
           elseif ($recordInput) { 'seed-missing' }
           else { 'seed-empty-input' }

$result = [pscustomobject]@{
    TaskKey                = $TaskKey
    Method                 = 'raw-regex'
    Record_UserInputPresent = [bool]$recordInput
    Data_UserInputPresent   = [bool]$dataInput
    History_TextCount       = $textCount
    NodeCount               = $nodeCount
    Verdict                 = $verdict
    TaskRaw                 = $taskRaw
    HistoryRaw              = $historyRaw
}

if ($Json) { $result | ConvertTo-Json -Depth 4 } else { $result | Format-List }
