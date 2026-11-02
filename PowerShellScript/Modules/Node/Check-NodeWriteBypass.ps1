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

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force

$csFiles = @(Get-GateFiles -Root $wfmDir -Extension cs -Exclude '\\(obj|bin|_deprecated)\\' |
    ForEach-Object { Get-Item -LiteralPath $_ })

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

# ---------------- H. 已并入 J（D11 去重） ----------------
# 原 H 段只扫运行态面两个文件、J 段扫全 Agent 面 + 白名单；两段同时命中即重复报告。
# 判据合并为 J 段单一扫描（白名单 AgentSqlNodeCarrier.cs），并在 J 的写原语集补 Delete/DeleteMany。

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

# ---------------- J. Agent 节点写单源（J1 · Error） ----------------
# 判据：Agent 生产代码中 INodeStore 写原语（PutAsync）调用只允许出现在唯一载体文件 AgentSqlNodeCarrier.cs；
#       删除原语（DeleteAsync/DeleteManyAsync）仅在运行态面文件窄扫描（保留原 H 段覆盖面，不把既有非写门删除点扩散成红）。
$agentSrcDir = Join-Path $Root 'Projects\Plug\Agent\src'
$agentWriteAllowed = @('AgentSqlNodeCarrier.cs')
$runtimeSurfaceLeaves = @('ProcessNodes.cs', 'NodeDomainPort.cs')
function Test-AgentNodeWrite([string]$file, [string]$text) {
    if ($text -match '\b(nodes|nodeStore|_nodes|store)\.PutAsync\s*\(') {
        $leaf = Split-Path $file -Leaf
        if ($agentWriteAllowed -notcontains $leaf) {
            return "[J/J1] $file 直接调用 INodeStore 写原语（唯一写点 = AgentSqlNodeCarrier.cs）"
        }
    }
    return $null
}
if (Test-Path $agentSrcDir)
{
    $agentFiles = @(Get-ChildItem -LiteralPath $agentSrcDir -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' })
    foreach ($file in $agentFiles) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $hit = Test-AgentNodeWrite $file.FullName $text
        if ($hit) { $violations.Add($hit) }
        # 运行态面窄扫描（原 H 段覆盖保留）：Delete/DeleteMany 同属裸写，按 J 单码报告（不再单列 H 段）。
        if ($runtimeSurfaceLeaves -contains (Split-Path $file.FullName -Leaf) -and
            $text -match '\b(nodes|nodeStore|_nodes|store)\.(DeleteAsync|DeleteManyAsync)\s*\(') {
            $violations.Add("[J/FW-1] $($file.FullName) 运行态面结算路径直接调用删除原语（须经唯一写门 NodeMaterializer）")
        }
    }
    # 负对照：合成裸写在非白名单文件必须被同一判据捕获。
    if (-not (Test-AgentNodeWrite 'Agent.Ui/Forbidden.cs' 'await nodes.PutAsync(record, ct);')) {
        $violations.Add('[J/J1] 负对照失效：合成裸写未被判据捕获')
    }
}
elseif ($ShowDetail) { Write-Host "SKIP(J): 未找到 Agent 源码目录（Frame-only 检出）。" -ForegroundColor Gray }

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

Write-Host "  [成功] A/B/C/I/J 全部通过（告警 $($warnings.Count) 项）" -ForegroundColor Green
Write-Host ""
exit 0
