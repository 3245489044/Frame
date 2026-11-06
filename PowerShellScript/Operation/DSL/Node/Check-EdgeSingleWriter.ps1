#requires -Version 5.1
<#
.SYNOPSIS
    边写单原语门禁（EdgeSingleWriter）—— 每条归属边恰一个业务写调用点。

.DESCRIPTION
    判据载体 = 结构事实（声明面 [Relation] intent=ensure + 基线映射表 + 源码静态方法坐标）；
    执行者叙述不参与。权威: axioms:Strategy.SingleImplementation / SEP.JudgmentCarrierStructural。

      EW1 映射完备：HierarchyRef 归属边集 ⊆ 映射表边集（缺边 ⇒ 红）
      EW2 业务写者唯一：每条业务边恰 1 个 Role=business 的 Writer（0/≥2 ⇒ 红）
      EW3 写者坐标可解析：Writer='<相对路径>:<方法名>' 的文件存在且含该方法名
      EW4 声明对账：映射的 Operation 必须是 inten=ensure 的 [Relation] 且 edge 一致
      EW5 负对照（-NegativeControl）：合成双业务写者必须被判红

    模式：默认报告；-Check 违规 ⇒ 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name EdgeSingleWriter
$rows = @($baseline.Edges | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.Value) })

function Read-RelationEnsures([string]$path) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    foreach ($line in (Get-Content -LiteralPath $path)) {
        if ($line -notmatch '^\s*\[Relation\]\s+([A-Za-z0-9_\-]+)\s*:') { continue }
        $id = $Matches[1]
        $fields = @{}
        foreach ($m in [regex]::Matches($line, '([A-Za-z_]+)\s*=\s*([^|]+)')) {
            $fields[$m.Groups[1].Value] = $m.Groups[2].Value.Trim()
        }
        if ($fields['intent'] -ne 'ensure') { continue }
        $map[$id] = [pscustomobject]@{ Id = $id; Edge = $fields['edge'] }
    }
    return $map
}

function Read-HierarchyEdges([string]$path) {
    $names = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $path)) { return $names }
    $inBlock = $false
    foreach ($line in (Get-Content -LiteralPath $path)) {
        if ($line -match '^\s*\[Space\]\s+HierarchyRef\b') { $inBlock = $true; continue }
        if ($inBlock -and $line -match '^\s*\[') { $inBlock = $false }
        if ($inBlock -and $line -match '^\s+([A-Za-z0-9_\-]+)\s*$') { $names.Add($Matches[1]) }
    }
    return $names
}

function Test-WriterCoordinate([string]$root, [string]$writer) {
    $index = $writer.LastIndexOf(':')
    if ($index -le 0 -or ($index + 1) -ge $writer.Length) { return $false }
    $rel = $writer.Substring(0, $index) -replace '/', '\'
    $method = $writer.Substring($index + 1)
    $path = Join-Path $root $rel
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    return [regex]::IsMatch((Get-Content -LiteralPath $path -Raw), ('\b' + [regex]::Escape($method) + '\b'))
}

function Test-EdgeMap($map, $ensures, $hierarchyEdges, [string]$root) {
    $problems = New-Object System.Collections.Generic.List[string]
    $byEdge = @{}
    foreach ($row in $map) {
        $edge = [string]$row.Value
        if (-not $byEdge.ContainsKey($edge)) { $byEdge[$edge] = New-Object System.Collections.Generic.List[object] }
        $byEdge[$edge].Add($row)
    }

    foreach ($edge in $hierarchyEdges) {
        if (-not $byEdge.ContainsKey($edge)) { $problems.Add("EW1 归属边 $edge 缺写者映射（禁叙述式单写）") }
    }

    foreach ($edge in $byEdge.Keys) {
        $business = @($byEdge[$edge] | Where-Object { [string]$_.Role -eq 'business' })
        if ($business.Count -ne 1) {
            $problems.Add("EW2 归属边 $edge 业务写者=$($business.Count)（判据=1）")
        }
        foreach ($row in $business) {
            $operation = [string]$row.Operation
            if (-not $ensures.ContainsKey($operation)) {
                $problems.Add("EW4 写者映射 operation='$operation' 不是 intent=ensure 的 [Relation]")
                continue
            }
            if ([string]$ensures[$operation].Edge -ne $edge) {
                $problems.Add("EW4 写者映射 operation='$operation' 的 edge=$($ensures[$operation].Edge) 与映射边 $edge 不一致")
            }
            if (-not (Test-WriterCoordinate -root $root -writer ([string]$row.Writer))) {
                $problems.Add("EW3 写者坐标不可解析：$edge → $($row.Writer)")
            }
        }
    }

    return $problems
}

$ensures = Read-RelationEnsures (Join-Path $Root 'Projects\Plug\Agent\Config\relations\core\relation-ops.ushar')
$hierarchyEdges = Read-HierarchyEdges (Join-Path $Root 'Projects\Plug\Agent\Config\nodes\foundation\reference.ushar')

if ($ensures.Count -eq 0) { Write-Host '[EdgeSingleWriter] relation-ops.ushar 无 ensure 声明（判据不可判定）' -ForegroundColor Red; if ($Check) { exit 1 }; exit 0 }
if ($hierarchyEdges.Count -eq 0) { Write-Host '[EdgeSingleWriter] reference.ushar 无 HierarchyRef 边集（判据不可判定）' -ForegroundColor Red; if ($Check) { exit 1 }; exit 0 }

$violations = @(Test-EdgeMap $rows $ensures $hierarchyEdges $Root)

if ($NegativeControl) {
    $probe = @(
        [pscustomobject]@{ Value = 'cross_db_link'; Operation = 'mount_node_task'; Writer = 'a.cs:M'; Role = 'business' }
        [pscustomobject]@{ Value = 'cross_db_link'; Operation = 'mount_node_task'; Writer = 'b.cs:M'; Role = 'business' }
    )
    $probeEnsures = @{ mount_node_task = [pscustomobject]@{ Id = 'mount_node_task'; Edge = 'cross_db_link' } }
    $probeHits = @(Test-EdgeMap $probe $probeEnsures @('cross_db_link') $Root)
    if (-not ($probeHits | Where-Object { $_ -like 'EW2 *' })) {
        Write-Host '[EdgeSingleWriter][NEGATIVE-CONTROL-FAILED] 合成双业务写者未被判红（判据空转）' -ForegroundColor Red
        exit 1
    }
    Write-Host "[EdgeSingleWriter][NEGATIVE-CONTROL-OK] 合成双写者命中 $($probeHits.Count) 项" -ForegroundColor Green
    exit 0
}

if ($ShowDetail) { foreach ($edge in $hierarchyEdges) { Write-Host "  · $edge" } }

if ($violations.Count -gt 0) {
    Write-Host "[EdgeSingleWriter] 违规 = $($violations.Count)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[EdgeSingleWriter] 归属边业务写者各 1（映射完备 / 坐标可解析）' -ForegroundColor Green
exit 0
