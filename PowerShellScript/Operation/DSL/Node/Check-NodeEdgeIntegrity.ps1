#requires -Version 5.1
<#
.SYNOPSIS
    节点边完整性守卫（NodeEdgeIntegrity）—— 「归属边跟随落库事实」的机器判据（I2 / P2-1 / P2-2）。

.DESCRIPTION
    C1 声明面：round-attribution.ushar 的 link_nodes inports 含 node_refs 且不含 node_keys
    C2 生产调用点：Projects/**/*.cs（非测试）中 ResolveProducedNodeKeys 调用点 = 0
    C3 实例身份：NodeInstanceRef 契约存在；NodeTaskLinker 不得按点截断实例键
    C4 兼容读：NodeOutputStore 实例键未命中须回落裸键
    C5 落库事实：SyncRelationsBlock / RoundAttributionFlow 均消费 PersistResult
    C6 边写断言：ReferenceGrounding 节点行不存在不得写边
    N1/N2 负对照：合成违规文本必须被同一判据函数命中（防恒真假绿）
    D1（可选 -AgentDb）：cross_db_link target 可在 ur_nodes 解析（设施不可用 ⇒ 报告态）

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [string]$AgentDb = ''
)

$ErrorActionPreference = 'Stop'

$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if (-not $Root) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$violations = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]

function Read-Text([string]$relative) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw
}

function Test-LinkNodesInports([string]$declarationText) {
    if ([string]::IsNullOrWhiteSpace($declarationText)) { return 'declaration-missing' }
    $line = ($declarationText -split "`n") | Where-Object { $_ -match '\[Node\]\s+link_nodes' } | Select-Object -First 1
    if (-not $line) { return 'link-nodes-node-missing' }
    if ($line -notmatch 'node_refs') { return 'inports-missing-node_refs' }
    if ($line -match 'node_keys') { return 'legacy-node_keys-still-declared' }
    return $null
}

function Test-EdgeAssertion([string]$groundingText) {
    if ([string]::IsNullOrWhiteSpace($groundingText)) { return 'reference-grounding-missing' }
    if ($groundingText -notmatch 'node-missing') { return 'edge-written-without-node-assertion' }
    return $null
}

$roundAttribution = Read-Text 'Projects\Plug\Agent\Config\workflows\attribution\round-attribution.ushar'
$reason = Test-LinkNodesInports $roundAttribution
if ($reason) { $violations.Add("C1 $reason") } elseif ($ShowDetail) { $details.Add('C1 link_nodes inports=node_refs OK') }

# ---- C2 生产调用点 ----
$sourceRoots = @((Join-Path $Root 'Projects\Plug\Agent\src'))
$legacyCalls = @()
foreach ($src in $sourceRoots) {
    if (-not (Test-Path -LiteralPath $src)) { continue }
    $legacyCalls += Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Tests\\' -and $_.Name -notlike '*Tests.cs' } |
        Select-String -Pattern 'ResolveProducedNodeKeys\s*\(' -ErrorAction SilentlyContinue
}
if ($legacyCalls.Count -gt 0) {
    $violations.Add("C2 legacy-call-sites=$($legacyCalls.Count): " + (($legacyCalls | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join '; '))
} elseif ($ShowDetail) { $details.Add('C2 ResolveProducedNodeKeys 生产调用点=0 OK') }

# ---- C3 实例身份 ----
$instanceContract = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Instances\NodeInstanceRef.cs'
if (-not (Test-Path -LiteralPath $instanceContract)) { $violations.Add('C3 NodeInstanceRef 契约缺失') }
$linker = Read-Text 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Linkage\NodeTaskLinker.cs'
if ([string]::IsNullOrWhiteSpace($linker) -or $linker -notmatch 'IsInstanceStorageKey') {
    $violations.Add('C3 NodeTaskLinker 可能按点截断实例键（缺 IsInstanceStorageKey 守卫）')
} elseif ($ShowDetail) { $details.Add('C3 实例键截断守卫 OK') }

# ---- C4 兼容读 ----
$outputStore = Read-Text 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\NodeOutputStore.cs'
if ([string]::IsNullOrWhiteSpace($outputStore) -or $outputStore -notmatch 'StripInstance') {
    $violations.Add('C4 NodeOutputStore 缺实例键→裸键兼容读（StripInstance）')
} elseif ($ShowDetail) { $details.Add('C4 兼容读 OK') }

# ---- C5 落库事实消费 ----
$syncRelations = Read-Text 'Projects\Plug\Agent\src\Agent.Core\Orchestration\SessionWorkflow\NodeWrite\SyncRelationsBlock.cs'
$roundFlow = Read-Text 'Projects\Plug\Agent\src\Agent.Core\Engine\Nodes\Bookkeeping\RoundAttributionFlow.cs'
if ($syncRelations -notmatch 'PersistResult') { $violations.Add('C5 SyncRelationsBlock 未消费 PersistResult') }
if ($roundFlow -notmatch 'PersistResult') { $violations.Add('C5 RoundAttributionFlow 未消费 PersistResult') }

# ---- C6 边写断言 ----
$grounding = Read-Text 'Frame\CsharpPrj\Operation\DataStoreDriven\Domains\Hierarchy\TaskReferenceGrounding.cs'
$reason = Test-EdgeAssertion $grounding
if ($reason) { $violations.Add("C6 $reason") } elseif ($ShowDetail) { $details.Add('C6 边写节点存在断言 OK') }

# ---- N1/N2 负对照 ----
$negative1 = Test-LinkNodesInports '[Node] link_nodes: fn=link_nodes | inports=task_key,workflow_id,node_keys | outports=linked'
if (-not $negative1) { $violations.Add('N1 负对照失效（旧 inports 未被判红）') } elseif ($ShowDetail) { $details.Add("N1 负对照命中: $negative1 OK") }

$negative2 = Test-EdgeAssertion 'public Task EnsureTaskNodeLinkAsync(string a, string b, string c) { return AttachAsync(); }'
if (-not $negative2) { $violations.Add('N2 负对照失效（无边断言未被判红）') } elseif ($ShowDetail) { $details.Add("N2 负对照命中: $negative2 OK") }

# ---- D1 DB 审计（可选；不可用 ⇒ 报告态） ----
if ($AgentDb) {
    if (-not (Test-Path -LiteralPath $AgentDb)) {
        $details.Add("D1 skip: agent.db 不存在（$AgentDb）")
    } else {
        $reader = Join-Path $Root 'Frame\PowerShellScript\Share\Data\Read-AgentDb.ps1'
        if (-not (Test-Path -LiteralPath $reader)) {
            $details.Add('D1 skip: Read-AgentDb.ps1 未装配')
        } else {
            try {
                $refs = & $reader -Store graph -Collection ur_references -Json 2>$null | ConvertFrom-Json
                $nodes = & $reader -Store agent -Table ur_nodes -Json 2>$null | ConvertFrom-Json
                $nodeKeys = @($nodes | ForEach-Object { $_.NodeKey })
                $phantoms = @($refs | Where-Object {
                    $_.RefType -eq 'cross_db_link' -and $_.Target -and $_.Target.Id -and
                    ($nodeKeys -notcontains (($_.Target.Id -split '\.')[-1]))
                })
                if ($phantoms.Count -gt 0) { $violations.Add("D1 phantom-cross_db_link=$($phantoms.Count)") }
                elseif ($ShowDetail) { $details.Add('D1 cross_db_link 0 幻影 OK') }
            } catch {
                $details.Add("D1 skip: DB 审计不可用（$($_.Exception.Message)）")
            }
        }
    }
}

if ($ShowDetail) { foreach ($line in $details) { Write-Host "  $line" } }

if ($violations.Count -eq 0) {
    Write-Host 'NodeEdgeIntegrity: PASS'
    exit 0
}

foreach ($violation in $violations) { Write-Host "  VIOLATION: $violation" }
if ($Check) { exit 1 }
exit 0

