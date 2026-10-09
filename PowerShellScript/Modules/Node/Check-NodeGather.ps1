#requires -Version 5.1
<#
.SYNOPSIS
    节点聚拢通用框架（Modules.Node/Gather）声明与接线不变式门禁（G1–G3）。

.DESCRIPTION
    治理目标（《节点聚拢通用框架-后续应用规划》§5）：把「多源读 → 单一化 → 按轴聚拢 → 塑形」收成一个通用框架，
    框架零领域知识、域差异全部由业务侧注入、装配点唯一。检查项（命中即违规）：
      G1 声明闭合：Config\nodes\node-gather-*.json 的 kind-shaping[*].family ⊆ families
         （族闭合：塑形声明的归宿族必须在 families 中声明；task-node 等空 families 域跳过）；有 families 却无 '*' 默认塑形 ⇒ 未知 kind 无归宿。
      G2 零领域：Frame\CsharpPrj\Modules\Node\Gather\*.cs 零上层 using（Operation/Share/Agent/Usharp）且零域键字面量
         （process/task-node/conversation/segment/message —— 域键属业务侧，框架不得硬编码）。
      G3 接线唯一：INodeGatherService 注册点恰 1（= NodeGatherHosting.TryAttachAll）；读适配**实现**不得落在框架层
         （§1.3：契约在框架、实现在业务）。
    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（聚拢框架架构回归）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER ShowDetail
    输出全部检查详情（默认仅输出违规摘要与计数）。

.EXAMPLE
    .\Check-NodeGather.ps1
    .\Check-NodeGather.ps1 -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NodeGateCommon.ps1')

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()

$gatherDir     = Join-Path $Root 'Frame\CsharpPrj\Modules\Node\Gather'
$agentNodesDir = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
$agentSrc      = Join-Path $Root 'Projects\Plug\Agent\src'

Write-GateHeader '节点聚拢通用框架门禁（G1–G3：声明闭合 / 零领域 / 接线唯一）'

# ---------------- G1 声明闭合（U9：真源 = node-gather.ushar；族闭合判据由语言闸A GatherGateRule US7101–US7106 承担） ----------------
# U9 退役 node-gather-*.json ⇒ 本段仅保留**正对照**（声明真源在册）；闭合/塑形判据不在此重写（复用闸A）。
$declFiles = @(Get-ChildItem -Path $agentNodesDir -Filter 'node-gather.ushar' -File -ErrorAction SilentlyContinue)
if ($declFiles.Count -lt 1)
{
    $violations.Add("G1 正对照失效：未找到 Config\nodes\node-gather.ushar（聚拢声明真源缺失 / 扫描面失效）")
}

# ---------------- G2 零领域（框架层 Gather 纯净性） ----------------
$forbiddenUsing = 'using\s+(Operation|Share|Agent|Usharp)\.'
$domainLiteral  = '"(process|task-node|conversation|segment|message)"'
foreach ($file in (Get-ChildItem -LiteralPath $gatherDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue))
{
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName))
    {
        $t = $line.TrimStart()
        if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
        if ($line -match $forbiddenUsing)
        {
            $violations.Add("G2 上层 using：$($file.Name)：$($line.Trim())")
        }
        if ($line -match $domainLiteral)
        {
            $violations.Add("G2 域键字面量：$($file.Name)：$($line.Trim())")
        }
    }
}

# ---------------- G3 接线唯一 ----------------
# G3a：INodeGatherService 注册点恰 1（唯一装配点 = NodeGatherHosting.TryAttachAll）。
$regHits = @(Get-ChildItem -Path $agentSrc -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\\\(obj|bin)\\\\' -and $_.FullName -notlike '*\_deprecated\*' } |
    Select-String -Pattern 'Register<INodeGatherService>')
if ($regHits.Count -ne 1)
{
    $names = [string]::Join('、', @($regHits | ForEach-Object { Split-Path $_.Path -Leaf }))
    $violations.Add("G3 接线点非唯一：Register<INodeGatherService> 命中 $($regHits.Count) 处（须恰 1 = NodeGatherHosting.TryAttachAll）：$names")
}

# G3b：框架层不得消费 Agent 侧域端口（契约在框架、实现在业务，§1.3）——「域适配器」实现须落 Agent.Modules；
# 框架仅保留与域无关的通用组合子（如 CompositeNodeReadAdapter）。
$adapterImpl = @(Get-ChildItem -LiteralPath $gatherDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
    Select-String -Pattern 'INodeDomainPort')
foreach ($hit in $adapterImpl)
{
    $violations.Add("G3 框架层消费域端口：$([System.IO.Path]::GetFileName($hit.Path))（点 §1.3：域适配器实现须在业务层）")
}

if ($ShowDetail)
{
    Write-Host "  G1 声明真源：$($declFiles.Count) 个 node-gather.ushar（闭合判据归闸A）" -ForegroundColor DarkGray
    Write-Host "  G2 扫描框架：$gatherDir" -ForegroundColor DarkGray
}

Write-GateVerdict -Violations $violations -Check:$true -Key 'NodeGather'
