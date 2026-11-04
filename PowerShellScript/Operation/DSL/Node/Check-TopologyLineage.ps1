#requires -Version 5.1
<#
.SYNOPSIS
    拓扑血缘门禁（TopologyLineage）—— C6：声明 derivces 可复算且生产调用点已通。

.DESCRIPTION
    判据载体 = 声明面静态结构 + 源码静态调用点（结构事实）；执行者叙述不参与。
    权威: axioms:AX.DimensionalityReduction / formula:SM.EffectiveFaceAssertion。

      TL1 血缘项非空：每个声明 derives= 的产品须有 ≥1 字段对且形态合法（src->dst）
      TL2 端点闭合：每对 dst ∈ 本产品 out；src ∈ 某一 depends_on 上游 out
      TL3 数据边唯一：同一 (from|to|derives) 只允许一条声明（重复 ⇒ 红）
      TL4 生产调用点：DeclarativeTopologyLineage 与 PlanFromField 在生产文件各 ≥1 处
      TL5 负对照（-NegativeControl）：合成非法血缘 / 缺失调用点必须被判红

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
$baseline = Import-GateBaseline -Name TopologyLineage
$productPath = Join-Path $Root ([string]$baseline.ProductGraph.Value -replace '/', '\')
$productionPath = Join-Path $Root ([string]$baseline.ProductionFile.Value -replace '/', '\')
$minEdges = [int]$baseline.MinLineageEdges.Value

function Read-Products([string]$path) {
    $items = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $path)) { return $items }
    foreach ($line in (Get-Content -LiteralPath $path)) {
        if ($line -notmatch '^\s*\[Product\]\s+([A-Za-z0-9_.\-]+)\s*:') { continue }
        $fields = @{}
        foreach ($m in [regex]::Matches($line, '([A-Za-z_]+)\s*=\s*([^|]+)')) {
            $fields[$m.Groups[1].Value] = $m.Groups[2].Value.Trim()
        }
        $items.Add([pscustomobject]@{
            Id        = $Matches[1]
            Out       = @(([string]$fields['out']) -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            DependsOn = @(([string]$fields['depends_on']) -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            Derives   = [string]$fields['derives']
        })
    }
    return $items
}

function Test-Lineage($products, [string]$productionText) {
    $problems = New-Object System.Collections.Generic.List[string]
    $byId = @{}
    foreach ($p in $products) { if (-not $byId.ContainsKey($p.Id)) { $byId[$p.Id] = $p } }

    $lineage = @($products | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Derives) })
    if ($lineage.Count -lt 1) { $problems.Add('TL1 声明面无可复算血缘项（derives= 全空）') }

    $edgeKeys = @{}
    foreach ($p in $lineage) {
        $pairs = @($p.Derives -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($pairs.Count -eq 0) { $problems.Add("TL1 $($p.Id) derives= 为空"); continue }
        if ($p.DependsOn.Count -eq 0) { $problems.Add("TL2 $($p.Id) 缺 depends_on（血缘无上游）"); continue }

        $upstreamOut = @()
        foreach ($parent in $p.DependsOn) {
            if (-not $byId.ContainsKey($parent)) { $problems.Add("TL2 $($p.Id) 上游未声明：$parent"); continue }
            $upstreamOut += @($byId[$parent].Out)
        }

        foreach ($pair in $pairs) {
            $index = $pair.IndexOf('->', [System.StringComparison]::Ordinal)
            if ($index -le 0 -or ($index + 2) -ge $pair.Length) { $problems.Add("TL1 $($p.Id) 字段对形态非法：$pair"); continue }
            $from = $pair.Substring(0, $index).Trim()
            $to = $pair.Substring($index + 2).Trim()
            if ($p.Out -notcontains $to) { $problems.Add("TL2 $($p.Id) 目标字段越权：$to ∉ out") }
            if ($upstreamOut -notcontains $from) { $problems.Add("TL2 $($p.Id) 源字段不在上游 out：$from ∉ [$($p.DependsOn -join ',')].out") }
        }

        $key = ('{0}|{1}|{2}' -f ($p.DependsOn -join ','), $p.Id, $p.Derives)
        if ($edgeKeys.ContainsKey($key)) { $problems.Add("TL3 数据边重复声明：$($p.Id)") } else { $edgeKeys[$key] = $true }
    }

    if ($lineage.Count -lt $minEdges) { $problems.Add("TL3 血缘边数=$($lineage.Count) < 基线 $minEdges") }

    if ($null -ne $productionText) {
        if ($productionText -notmatch '\bDeclarativeTopologyLineage\b') { $problems.Add('TL4 生产调用点缺失：DeclarativeTopologyLineage') }
        if ($productionText -notmatch '\bPlanFromField\b') { $problems.Add('TL4 生产调用点缺失：PlanFromField') }
    }

    return $problems
}

if ($NegativeControl) {
    $probeProducts = @(
        [pscustomobject]@{ Id = 'a.p'; Out = @('a'); DependsOn = @(); Derives = 'x->y'; }
    )
    $probe = @(Test-Lineage $probeProducts 'class P { }')
    if ($probe.Count -lt 2) {
        Write-Host '[TopologyLineage][NEGATIVE-CONTROL-FAILED] 合成非法血缘/缺失调用点未被判红' -ForegroundColor Red
        exit 1
    }
    Write-Host "[TopologyLineage][NEGATIVE-CONTROL-OK] 合成样本命中 $($probe.Count) 项" -ForegroundColor Green
    exit 0
}

if (-not (Test-Path -LiteralPath $productPath)) {
    Write-Host "[TopologyLineage] 产品声明面不存在：$productPath" -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

$products = Read-Products $productPath
$productionText = if (Test-Path -LiteralPath $productionPath) { Get-Content -LiteralPath $productionPath -Raw } else { $null }
$violations = @(Test-Lineage $products $productionText)

if ($ShowDetail) {
    Write-Host "  · 产品 = $($products.Count) / 血缘项 = $(@($products | Where-Object { $_.Derives }).Count) / 生产文件 = $productionPath"
}

if ($violations.Count -gt 0) {
    Write-Host "[TopologyLineage] 违规 = $($violations.Count)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[TopologyLineage] 声明血缘可复算 + 生产调用点已通（C6）' -ForegroundColor Green
exit 0
