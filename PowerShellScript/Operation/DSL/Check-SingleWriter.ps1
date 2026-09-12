#requires -Version 5.1
<#
.SYNOPSIS
    SQLite 写串行化架构不变式审计（对齐 2026-09-03-db-read-hang-arch-refactor.md 收敛判据）。

.DESCRIPTION
    校验 SQLite 存取的关键架构不变式（写收敛到库级写通道、读不绑进程写门），取代旧「WriteCoordinator 全库唯一写门」判定——
    该设计已回退（见 2026-09-01-write-serialization-arch-defects.md），SqliteTransaction 走 SqliteWriteChannel 单写连接、
    DataUniversal 走按库有界写门，均合法，不应再要求它们显式调用已回退的 WriteCoordinator.Run*。
    检查项（命中即违规/告警）：
      A. DataUniversal 不得残留「全局 object Gate + lock(Gate)」的无界进程写门（读/写跨库互串 + 无界阻塞之源）；
         写应落在「按库单写门（SemaphoreSlim + 有界等待）」或 SqliteWriteChannel 单写连接。
      B. 读路径必须与进程写门解耦：SqliteRelate.ExecuteQueryCore 应走只读连接 SetReadConnect，而非持写门。
      C. Agent 事务写路径经 SqliteWriteChannel 单写通道（SqliteTransaction.Acquire），同域串行化。
      D. （告警，不判失败）旧残留：lock (Gate) / SqliteWriteGate 用法。
    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（架构回归）。

.PARAMETER Root
    扫描根目录（默认仓库根，相对本脚本上溯四级）。

.PARAMETER ShowDetail
    输出全部命中/检查详情（默认仅输出违规摘要）。

.EXAMPLE
    .\Check-SingleWriter.ps1
    .\Check-SingleWriter.ps1 -Root d:\Project\Program\WorkFlowPrj
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根（与 Check-AgentRuntimeSlots.ps1 一致）
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

$csFiles = Get-ChildItem -Path $Root -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

$relate = $csFiles | Where-Object { $_.Name -eq 'SqliteRelate.cs' } | Select-Object -First 1
$tx     = $csFiles | Where-Object { $_.Name -eq 'SqliteTransaction.cs' } | Select-Object -First 1

# A. DataUniversal 不得残留全局 object Gate / lock(Gate) 无界进程写门。
if ($relate)
{
    if (Select-String -Path $relate.FullName -Pattern 'private static readonly object Gate' -ErrorAction SilentlyContinue)
    {
        $violations.Add("SqliteRelate.cs 残留全局写门 'object Gate'（无界 Monitor 进程锁；应改按库有界写门或 SqliteWriteChannel）")
    }
    if (Select-String -Path $relate.FullName -Pattern 'lock\s*\(\s*Gate\s*\)' -ErrorAction SilentlyContinue)
    {
        $violations.Add("SqliteRelate.cs 存在 'lock(Gate)' 用法（写/读未与无界进程门解耦）")
    }
    if (-not (Select-String -Path $relate.FullName -Pattern 'SemaphoreSlim\s+WriteGates|WriteGateWaitMs|SqliteWriteChannel' -ErrorAction SilentlyContinue))
    {
        $warnings.Add("SqliteRelate.cs 未检测到按库有界写门或 SqliteWriteChannel（中间态可接受，确认写确有界）")
    }
    elseif ($ShowDetail)
    {
        Write-Host "OK(A): SqliteRelate.cs 写门有界（按库 SemaphoreSlim 或 SqliteWriteChannel），无全局 object Gate。"
    }
}
else
{
    $violations.Add("缺失关键存储文件 SqliteRelate.cs（无法审计）")
}

# B. 读路径与进程写门解耦：ExecuteQueryCore 应走只读连接（SetReadConnect），而非持写门。
if ($relate)
{
    $hasReadConnect = Select-String -Path $relate.FullName -Pattern 'SetReadConnect\(' -ErrorAction SilentlyContinue
    $qInLock = Select-String -Path $relate.FullName -Pattern 'ExecuteQueryCore.*' -ErrorAction SilentlyContinue
    if (-not $hasReadConnect)
    {
        $warnings.Add("SqliteRelate.cs 未见 SetReadConnect 只读连接（读仍未与写连接/WAL pragma 解耦）")
    }
    elseif ($ShowDetail)
    {
        Write-Host "OK(B): SqliteRelate.cs 读路径经只读连接 SetReadConnect（不进进程写门）。"
    }
}

# C. Agent 事务写路径经 SqliteWriteChannel 单写通道。
if ($tx)
{
    if (Select-String -Path $tx.FullName -Pattern 'SqliteWriteChannel\.Acquire|SqliteWriteLease' -ErrorAction SilentlyContinue)
    {
        if ($ShowDetail) { Write-Host "OK(C): SqliteTransaction.cs 经 SqliteWriteChannel 单写连接（同域串行化）。" }
    }
    else
    {
        $warnings.Add("SqliteTransaction.cs 未检测到 SqliteWriteChannel.Acquire（确认事务写是否仍收敛到单写通道）")
    }
}
else
{
    $warnings.Add("缺失 SqliteTransaction.cs（无法审计事务写通道）")
}

# D. 旧残留（告警，不判失败）：lock(Gate) / SqliteWriteGate 在其余代码库的散落用法。
foreach ($pat in @('lock (Gate)', 'lock(Gate)', 'SqliteWriteGate.Run'))
{
    $hits = $csFiles | Where-Object { $_.Name -ne 'SqliteRelate.cs' } |
        Select-String -Pattern $pat -SimpleMatch -ErrorAction SilentlyContinue
    foreach ($h in $hits)
    {
        $warnings.Add("旧残留[$pat] $($h.Path):$($h.LineNumber)（确认是否仍绕写门）")
    }
}

# E. 节点输出端口写（ur_nodes）必须经确定性写门（DP-1）：端口写调用点只允许集中在 NodeExecutionService（写门持有者）。
#    白名单 = 已登记剩余项（AgentUiLibraryConversation：UI 会话节点直写，待共享写门注入面落地后移出，见数据流聚拢点分析 DP-1-R1）。
$nodePortWriteWhitelist = @('AgentUiLibraryConversation.cs')
$agentSrcFiles = $csFiles | Where-Object { $_.FullName -match '\\Projects\\Plug\\Agent\\src\\' }
$bypassHits = $agentSrcFiles |
    Where-Object { $_.Name -ne 'NodeExecutionService.cs' -and $nodePortWriteWhitelist -notcontains $_.Name } |
    Select-String -Pattern '\.WriteOutputAsync\(' -ErrorAction SilentlyContinue
foreach ($h in $bypassHits)
{
    $violations.Add("节点输出端口写旁路确定性写门（DP-1）$($h.Path):$($h.LineNumber)")
}
if (-not $bypassHits -and $ShowDetail)
{
    Write-Host "OK(E): ur_nodes 端口写调用点仅 NodeExecutionService（经确定性写门），白名单 $($nodePortWriteWhitelist.Count) 项。"
}

foreach ($w in $warnings)
{
    if ($ShowDetail) { Write-Host "WARN: $w" -ForegroundColor Yellow }
}

if ($violations.Count -eq 0)
{
    Write-Host "Check-SingleWriter: OK —— 无违规（写收敛到库级通道 + 读不与进程写门耦合）。告警 $($warnings.Count) 条。" -ForegroundColor Green
    exit 0
}

Write-Host "Check-SingleWriter: FAIL —— 发现 $($violations.Count) 处违规：" -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
exit 1

