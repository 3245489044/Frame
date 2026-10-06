#requires -Version 5.1
<#
.SYNOPSIS
    P6-A 委托接线架构不变式门禁 —— 键格式声明化 / 端口准入单源 / 节点写路径单源。

.DESCRIPTION
    对 Frame/CsharpPrj/Operation/WorkFlowMethod（P6-A 委托接线面）执行三类静态判定：

      A. 层次化键格式声明化（A-3 / P14）：NodeProcess\KeyMapping 内 `AsyncLocal` 命中 = 0
         （键格式必须来自 KeyHierarchyFormat 声明，运行期只读，多工作流并发不串味）。
      B. 端口准入单源（A-1.1/A-1.2 / G2）：`ContainsExecutionParameter(...)` 调用点 = 0
         （裸端口「先到者胜」判定唯一实现 = Modules.Node.Ports.PortKeySpace；禁自建第二实现）。
      C. 节点写路径单源（A-1.4 / G3 / P16）：`INodeOutputStore.WriteOutputAsync` 的生产调用点
         必须全部落在白名单文件 `NodeOutputPersistStage.cs`（经 NodePersistCoordinator → NodeWriteGate）。
      D. （告警，不判失败）NodeProcess 内 `new PortKeySpace` 之外的疑似裸键容器（HashSet<string>）。
      H. 运行态面结算单源（FW-1/FW-5）：内存运行态面（过程节点）的结算路径禁直接写存储
         （`INodeStore.PutAsync` / `DeleteAsync` 等须经唯一写门 NodeMaterializer；路径缺失即跳过）。
      I. Stream 形态落库恰 1 次写（P5 · Error）：NodeDomainPort.CommitRunAsync 的 Stream 结算须恰 1 次
         WriteAsync 落库且无循环（防退回逐帧写）；附负对照（循环 + 多次写合成体必须被捕获）。

    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（架构回归）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER ShowDetail
    输出全部检查详情（默认仅输出违规/告警摘要）。

.EXAMPLE
    .\Check-NodeWriteBypass.ps1
    .\Check-NodeWriteBypass.ps1 -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

$wfmDir = Join-Path $Root 'Frame\CsharpPrj\Operation\WorkFlowMethod'

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  P6-A 委托接线门禁（键格式声明化 / 端口准入单源 / 写路径单源）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path $wfmDir))
{
    Write-Host "  [失败] 缺失工程目录: $wfmDir" -ForegroundColor Red
    exit 1
}

$csFiles = @(Get-ChildItem -Path $wfmDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' })

# ---------------- A. 键格式声明化（A-3 / P14） ----------------
$keyMappingDir = Join-Path $wfmDir 'NodeProcess\KeyMapping'
$keyMappingFiles = @($csFiles | Where-Object { $_.FullName -like "$keyMappingDir\*" })
$asyncLocalHits = @($keyMappingFiles | Select-String -Pattern 'AsyncLocal|static\s+(string|int)\s+\w+\s*\{\s*get;\s*set;\s*\}' -ErrorAction SilentlyContinue |
    Where-Object { $_.Line.TrimStart() -notmatch '^(//|/\*|\*)' })
if ($asyncLocalHits.Count -gt 0)
{
    foreach ($h in $asyncLocalHits) { $violations.Add("[A/P14] $($h.Path):$($h.LineNumber) 键格式出现可变进程静态（须经 KeyHierarchyFormat 声明）") }
}
elseif ($ShowDetail) { Write-Host "OK(A): KeyMapping 零 AsyncLocal/可写静态配置。" -ForegroundColor Green }

# ---------------- B. 端口准入单源（A-1.1/A-1.2 / G2） ----------------
$portRuleHits = @($csFiles | Select-String -Pattern 'ContainsExecutionParameter\s*\(' -ErrorAction SilentlyContinue |
    Where-Object { $_.Line -notmatch 'public\s+bool\s+ContainsExecutionParameter' })
if ($portRuleHits.Count -gt 0)
{
    foreach ($h in $portRuleHits) { $violations.Add("[B/G2] $($h.Path):$($h.LineNumber) 自建裸端口准入判定（唯一实现 = PortKeySpace）") }
}
elseif ($ShowDetail) { Write-Host "OK(B): 裸端口准入判定零旁路调用点。" -ForegroundColor Green }

# ---------------- C. 节点写路径单源（A-1.4 / G3 / P16） ----------------
$writeWhitelist = @('NodeOutputPersistStage.cs')
$writeHits = @($csFiles | Select-String -Pattern '\.WriteOutputAsync\s*\(' -ErrorAction SilentlyContinue |
    Where-Object { $writeWhitelist -notcontains (Split-Path $_.Path -Leaf) })
if ($writeHits.Count -gt 0)
{
    foreach ($h in $writeHits) { $violations.Add("[C/G3] $($h.Path):$($h.LineNumber) 节点输出写绕过单写门（唯一写入口 = NodeOutputPersistStage → NodeWriteGate）") }
}
elseif ($ShowDetail) { Write-Host "OK(C): 节点输出写路径唯一（NodeOutputPersistStage）。" -ForegroundColor Green }

# ---------------- H. 运行态面结算必须经唯一写门（FW-5 · Error） ----------------
# 判据：内存运行态面（过程节点等）的结算路径**禁**直接写存储（<c>INodeStore.PutAsync</c> 等裸写）；
#       结算必须经唯一写门（NodeMaterializer → NodeWriteGate → 载体）。扫描面 = 内存面定义 + 域端口实现
#       （Agent 侧；路径缺失即跳过，保持 Frame-only 检出可用）。
$runtimeSurfaceFiles = @(
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Signal\ProcessNodes.cs'),
    (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\NodeSpec\NodeDomainPort.cs')
)
foreach ($f in $runtimeSurfaceFiles)
{
    if (-not (Test-Path $f)) { continue }
    $bareWriteHits = @(Select-String -Path $f -Pattern '\.(PutAsync|DeleteAsync|DeleteManyAsync)\s*\(' -ErrorAction SilentlyContinue |
        Where-Object { $_.Line.TrimStart() -notmatch '^(//|/\*|\*)' })
    foreach ($h in $bareWriteHits)
    {
        $violations.Add("[H/FW-1] $(Split-Path $h.Path -Leaf):$($h.LineNumber) 运行态面结算路径直接写存储（须经唯一写门 NodeMaterializer）")
    }
}

# ---------------- I. Stream 形态落库恰 1 次写（P5 · Error；防退回逐帧写） ----------------
# 判据：运行态域结算 CommitRunAsync 的 Stream 形态把整任务帧聚合成**一个**载荷 ⇒ 经唯一写门**恰 1 次**
#       WriteAsync 落库（禁 foreach/for/while 逐帧写）。载体 = NodeDomainPort.cs 的 CommitRunAsync 方法体（结构事实）。
#       该文件缺失即跳过（Frame-only 检出仍可用）。
$portFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\NodeSpec\NodeDomainPort.cs'
if (Test-Path $portFile) {
    $portText = [System.IO.File]::ReadAllText($portFile)
    $startIdx = $portText.IndexOf('Task<IReadOnlyList<NodeWriteOutcome>> CommitRunAsync')
    $endIdx   = $portText.IndexOf('private string? StreamPlacementIdOf')
    if ($startIdx -lt 0 -or $endIdx -le $startIdx) {
        $violations.Add("[I/P5] NodeDomainPort.cs: 无法定位 CommitRunAsync 方法体（判据不可判）")
    }
    else {
        $bodyText = $portText.Substring($startIdx, $endIdx - $startIdx)
        $codeLines = @($bodyText -split "`r?`n" | Where-Object { $_.TrimStart() -notmatch '^(//|/\*|\*)' })
        $code = [string]::Join("`n", $codeLines)
        $writeCount = ([regex]::Matches($code, '\bWriteAsync\s*\(')).Count
        $loopCount  = ([regex]::Matches($code, '\b(foreach|for|while)\s*\(')).Count
        if ($writeCount -ne 1) {
            $violations.Add("[I/P5] NodeDomainPort.cs: CommitRunAsync（Stream 结算）WriteAsync 调用数=$writeCount（须恰 1：整任务帧聚合成单载荷）")
        }
        if ($loopCount -ne 0) {
            $violations.Add("[I/P5] NodeDomainPort.cs: CommitRunAsync（Stream 结算）出现循环 $loopCount 处（疑似退回逐帧写）")
        }
        # 负对照（防假绿）：逐帧写合成体（循环 + 多次写）必须被同一判据捕获。
        $probe = 'foreach (var f in frames) { await WriteAsync(f, ct); } await WriteAsync(agg, ct);'
        $probeWrites = ([regex]::Matches($probe, '\bWriteAsync\s*\(')).Count
        $probeLoops  = ([regex]::Matches($probe, '\b(foreach|for|while)\s*\(')).Count
        if ($probeWrites -lt 2 -or $probeLoops -lt 1) {
            $violations.Add("[I/P5] 负对照失效：逐帧写合成体（循环 + 多次 WriteAsync）未被判据捕获")
        }
    }
}
elseif ($ShowDetail) { Write-Host "SKIP(I): 未找到 NodeDomainPort.cs（Frame-only 检出）。" -ForegroundColor Gray }

# ---------------- D. （告警）疑似裸键容器 ----------------
$bareContainerHits = @($csFiles | Select-String -Pattern 'HashSet<string>\s+\w*[Bb]are' -ErrorAction SilentlyContinue)
foreach ($h in $bareContainerHits) { $warnings.Add("[D/G2] $($h.FullName):$($h.LineNumber) 疑似自建裸键容器（应经 PortKeySpace）") }

# ---------------- 汇总 ----------------
Write-Host ""
Write-Host "  扫描源码文件: $($csFiles.Count) 个" -ForegroundColor Gray

if ($warnings.Count -gt 0)
{
    Write-Host "  [告警] $($warnings.Count) 项：" -ForegroundColor Yellow
    foreach ($w in $warnings) { Write-Host "    - $w" -ForegroundColor Yellow }
}

if ($violations.Count -gt 0)
{
    Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
    Write-Host ""
    Write-Host "  结果: 失败（架构回归）" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host "  [成功] A/B/C/I 全部通过（告警 $($warnings.Count) 项）" -ForegroundColor Green
Write-Host ""
exit 0
