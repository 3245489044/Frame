# Check-EdgeAsVertex.ps1
# 边一等化判据（DP2 / X2）：投影产物中边只进 Edges 通道，永不进 Nodes 通道。
# 结构事实载体：LineageProjector.cs 内节点/边通道的装配调用计数（非叙述）。
# 判据（-Check 时非零退出）：
#   ① AddNodes( 恰 1 处、AddEdges( 恰 1 处（双通道唯一装配点）
#   ② 节点集合追加形态 'nodes.Add(' 计数 = 0（边不得借节点集合落地）
#   ③ 边种类映射含 DataFlow / Composition / ControlFlow（族 ↔ 类型映射存在，禁单类型糊平）
# 负对照（单一实现，不在本脚本内重复）：DataCenter.Tests\DataflowProjectionTests 合成一条边后
#   断言 nodes.Count 不变、edges.Count +1。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$projector = Join-Path $Root 'Frame\DataCenter\DataCenter.Core\Dataflow\LineageProjector.cs'
if (-not (Test-Path -LiteralPath $projector)) {
    Write-Host "缺件：$projector（图投影器未落地）"
    if ($Check) { exit 1 }
    exit 0
}

$text = Get-Content -LiteralPath $projector -Raw
$violations = [System.Collections.Generic.List[string]]::new()

$addNodes = ([regex]::Matches($text, '\.AddNodes\(')).Count
$addEdges = ([regex]::Matches($text, '\.AddEdges\(')).Count
if ($addNodes -ne 1) { $violations.Add("AddNodes 装配点 = $addNodes（须恰 1）") }
if ($addEdges -ne 1) { $violations.Add("AddEdges 装配点 = $addEdges（须恰 1）") }

$nodeAppend = ([regex]::Matches($text, '\bnodes\.Add\(')).Count
if ($nodeAppend -gt 0) { $violations.Add("节点集合追加 $nodeAppend 处（边/顶点须单一来源，禁旁路 Add）") }

foreach ($type in @('CanonicalEdgeType.DataFlow', 'CanonicalEdgeType.Composition', 'CanonicalEdgeType.ControlFlow')) {
    if ($text -notmatch [regex]::Escape($type)) {
        $violations.Add("缺边族类型映射：$type")
    }
}

Write-Host ("Check-EdgeAsVertex: AddNodes={0} AddEdges={1} 节点旁路追加={2} 违规={3}" -f $addNodes, $addEdges, $nodeAppend, $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" }
    if ($Check) { exit 1 }
}

exit 0
