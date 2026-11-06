#requires -Version 5.1
<#
.SYNOPSIS
    Agent 语义观测实时读面（零落盘）—— 连接运行中的 Agent 进程的观测管道出口，实时读取（tail）或按 scope 回溯事件。

.DESCRIPTION
    Agent 侧语义观测已改为「有界内存环 + 命名管道出口」（零落盘）：出口名 = `agent-observe-<pid>[-n]`，
    本脚本按前缀枚举发现（无需落盘名册文件），连上后收 NDJSON 帧：

      {"t":"hello",...}                        连接首帧（回显 pid/pipe/ring 容量）
      {"t":"event","e":{...PersistedEvent...}} 实时/回放事件（行 schema 与旧 events.jsonl 完全一致）
      {"t":"backlogEnd",...} / {"t":"end",...} 回放/查询窗口封口（droppedTotal=生命周期累计丢弃；truncated=本次窗口被环截断）
      {"t":"items","events":[...]}             按 scope 查询命中批
      {"t":"error","message":...}              控制帧错误

    控制帧（本脚本发出的）：tail（tail + 可选 backlog 回放 + scope/kind 过滤）/ query（按 scope 取整条链）/ ping / quit。

.PARAMETER PipeName
    显式管道名（缺省 = 按 -PipePrefix 枚举；恰一个即连，多个须显式指定）。

.PARAMETER PipePrefix
    管道名前缀（缺省 agent-observe；须与 Agent 侧 AgentObservability.PipePrefix 一致）。

.PARAMETER List
    只列出当前在跑的观测出口实例名（不连接）。

.PARAMETER Probe
    探测模式：连接并在 -WindowSec 窗口内采集，返回摘要对象（Connected/Pipe/EventCount/Kinds/Events/Reason），不写宿主输出、不抛异常（供门禁调用）。

.PARAMETER ScopeId
    作用域过滤（Run=runId / Round=roundId，如 `task_x#R1`）；给定时仅收该作用域事件。

.PARAMETER Kind
    事件分类子串过滤（不区分大小写，如 NodeStep / ClineDiagnostics）。

.PARAMETER Backlog
    先回放内存环最近 N 条（0 = 只收实时）。

.PARAMETER Query
    单次查询模式：按 -ScopeId 取整条链后退出（需 -ScopeId）。

.PARAMETER Limit
    查询上限（缺省 500）。

.PARAMETER TimeoutMs
    连接与单次读等待毫秒（缺省 1500）。

.PARAMETER WindowSec
    采集窗口秒数（0 = 一直读到 Ctrl+C / 客户端断开）。

.PARAMETER MaxEvents
    收够 N 条即退出（0 = 不限）。

.PARAMETER Json
    输出原始事件 JSON（供 ConvertFrom-Json / 二次处理），否则输出单行摘要。

.PARAMETER Quiet
    静默：不输出事件与提示（仍以退出码表达成败）。

.EXAMPLE
    Invoke-Frame.ps1 -Action watch-observe -Backlog 200

.EXAMPLE
    Invoke-Frame.ps1 -Action watch-observe -ScopeId 'task_state_8679e047#R1' -Query -Json

.EXAMPLE
    Invoke-Frame.ps1 -Action watch-observe -List
#>
[CmdletBinding()]
param(
    [string]$PipeName = '',
    [string]$PipePrefix = 'agent-observe',
    [switch]$List,
    [switch]$Probe,
    [string]$ScopeId = '',
    [string]$Kind = '',
    [int]$Backlog = 0,
    [switch]$Query,
    [int]$Limit = 500,
    [int]$TimeoutMs = 1500,
    [int]$WindowSec = 0,
    [int]$MaxEvents = 0,
    [switch]$Json,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Get-ObservePipeNames
{
    [CmdletBinding()]
    param([string]$Prefix)
    try
    {
        return @([System.IO.Directory]::GetFiles('\\.\pipe\') |
            ForEach-Object { $_.Substring($_.LastIndexOf('\') + 1) } |
            Where-Object { $_ -like "$Prefix*" } |
            Sort-Object -Unique)
    }
    catch
    {
        return @()
    }
}

function Resolve-ObservePipeName
{
    [CmdletBinding()]
    param([string]$Name, [string]$Prefix)
    if (-not [string]::IsNullOrWhiteSpace($Name)) { return $Name }

    $candidates = @(Get-ObservePipeNames -Prefix $Prefix)
    if ($candidates.Count -eq 0)
    {
        throw "无运行中的观测出口（前缀 '$Prefix'）：Agent 未启动或未装配观测（期望管道名 '$Prefix-<pid>'）"
    }
    if ($candidates.Count -gt 1)
    {
        throw "观测出口有 $($candidates.Count) 个实例（$($candidates -join ', ')）⇒ 请用 -PipeName 指定"
    }
    return $candidates[0]
}

function Format-ObserveEvent
{
    [CmdletBinding()]
    param($e)
    $line = "{0} #{1} {2}" -f $e.ScopeId, $e.EventSeq, $e.Event.EventKind
    if ($e.Event.Phase) { $line += " phase=$($e.Event.Phase)" }
    if ($e.Event.Stage) { $line += " stage=$($e.Event.Stage)" }
    return $line
}

if ($List)
{
    $names = @(Get-ObservePipeNames -Prefix $PipePrefix)
    if ($names.Count -eq 0)
    {
        if (-not $Quiet) { Write-Host '  （无运行中的观测出口）' -ForegroundColor DarkGray }
        exit 0
    }
    foreach ($n in $names) { Write-Host "  $n" }
    exit 0
}

$resolved = ''
try { $resolved = Resolve-ObservePipeName -Name $PipeName -Prefix $PipePrefix }
catch
{
    if ($Probe)
    {
        return [pscustomobject]@{
            Connected = $false; Pipe = ''; EventCount = 0; Kinds = @(); Events = @(); Reason = $_.Exception.Message
        }
    }
    if (-not $Quiet) { Write-Error $_.Exception.Message }
    exit 1
}

$client = $null
$reader = $null
$writer = $null
$utf8 = New-Object System.Text.UTF8Encoding($false)
try
{
    $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $resolved, [System.IO.Pipes.PipeDirection]::InOut)
    try { $client.Connect($TimeoutMs) }
    catch { throw "连接观测出口失败（$resolved）：$($_.Exception.Message)" }

    $reader = New-Object System.IO.StreamReader($client, $utf8)
    $writer = New-Object System.IO.StreamWriter($client, $utf8)
    $writer.AutoFlush = $true

    if ($Query)
    {
        if ([string]::IsNullOrWhiteSpace($ScopeId)) { throw '-Query 须同时给 -ScopeId（按作用域取整条链）' }
        $frame = @{ t = 'query'; id = 1; scope = $ScopeId; limit = $Limit }
        $writer.WriteLine(($frame | ConvertTo-Json -Compress))
    }
    else
    {
        $frame = @{ t = 'tail'; backlog = [int]$Backlog }
        if (-not [string]::IsNullOrWhiteSpace($ScopeId)) { $frame['scope'] = $ScopeId }
        if (-not [string]::IsNullOrWhiteSpace($Kind)) { $frame['kind'] = $Kind }
        $writer.WriteLine(($frame | ConvertTo-Json -Compress))
    }

    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $events = New-Object System.Collections.Generic.List[object]
    $hello = $null

    while ($true)
    {
        if ($WindowSec -gt 0 -and $clock.Elapsed.TotalSeconds -ge $WindowSec) { break }
        if ($MaxEvents -gt 0 -and $events.Count -ge $MaxEvents) { break }
        if (-not $client.IsConnected) { break }

        $task = $reader.ReadLineAsync()
        if (-not $task.Wait([Math]::Max(50, $TimeoutMs)))
        {
            continue   # 本窗口内无帧：继续轮询（不视为失败）
        }

        $line = $task.Result
        if ($null -eq $line) { break }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }

        $obj = $null
        try { $obj = $line | ConvertFrom-Json } catch { continue }

        switch ([string]$obj.t)
        {
            'hello' { $hello = $obj }
            'event'
            {
                $events.Add($obj.e) | Out-Null
                if ($Probe -or $Quiet) { break }
                if ($Json) { $obj.e | ConvertTo-Json -Depth 12 -Compress }
                else { Write-Host (Format-ObserveEvent $obj.e) }
            }
            'items'
            {
                foreach ($e in @($obj.events))
                {
                    $events.Add($e) | Out-Null
                    if ($Probe -or $Quiet) { continue }
                    if ($Json) { $e | ConvertTo-Json -Depth 12 -Compress }
                    else { Write-Host (Format-ObserveEvent $e) }
                }
            }
            'backlogEnd'
            {
                if (-not ($Probe -or $Quiet))
                {
                    Write-Host ("  [回放封口] {0} 条 · ring={1} · 累计丢弃={2} · 本次截断={3} · 坏行={4}" -f $obj.count, $obj.ring, $obj.droppedTotal, $obj.truncated, $obj.corrupt) -ForegroundColor DarkGray
                }
                if ($MaxEvents -gt 0 -and $events.Count -ge $MaxEvents) { break }
            }
            'end'
            {
                if (-not ($Probe -or $Quiet))
                {
                    Write-Host ("  [查询封口] scope={0} · {1} 条 · ring={2} · 累计丢弃={3} · 本次截断={4} · nextAfterSeq={5} · 坏行={6}" -f $obj.scope, $obj.matched, $obj.ring, $obj.droppedTotal, $obj.truncated, $obj.nextAfterSeq, $obj.corrupt) -ForegroundColor DarkGray
                }
                if ($Query) { break }
            }
            'error'
            {
                if (-not ($Probe -or $Quiet)) { Write-Warning "观测出口错误：$($obj.message)" }
                if ($Query) { break }
            }
            default { }
        }
    }

    if ($Probe)
    {
        return [pscustomobject]@{
            Connected  = $true
            Pipe       = $resolved
            Ring       = if ($hello) { [int]$hello.ring } else { 0 }
            EventCount = $events.Count
            Kinds      = @($events | ForEach-Object { $_.Event.EventKind } | Sort-Object -Unique)
            Events     = @($events)
            Reason     = ''
        }
    }

    if (-not $Quiet -and $events.Count -eq 0)
    {
        Write-Host '  （窗口内无事件：Agent 可能空闲，或过滤条件过窄）' -ForegroundColor DarkGray
    }
    exit 0
}
catch
{
    if ($Probe)
    {
        return [pscustomobject]@{
            Connected = $false; Pipe = $resolved; Ring = 0; EventCount = 0; Kinds = @(); Events = @(); Reason = $_.Exception.Message
        }
    }
    if (-not $Quiet) { Write-Error $_.Exception.Message }
    exit 1
}
finally
{
    if ($writer) { try { $writer.WriteLine('{"t":"quit"}'); $writer.Flush() } catch { } ; try { $writer.Dispose() } catch { } }
    if ($reader) { try { $reader.Dispose() } catch { } }
    if ($client) { try { $client.Dispose() } catch { } }
}
