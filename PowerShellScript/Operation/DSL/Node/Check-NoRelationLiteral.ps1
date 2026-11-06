#requires -Version 5.1
<#
.SYNOPSIS
    关系字面量门禁（NoRelationLiteral）—— C# 关系边名只能来自单源常量。

.DESCRIPTION
    判据载体 = 源码静态字符串字面量（结构事实）；执行者叙述不参与。
    权威: axioms:Strategy.SingleImplementation / SEP.JudgmentCarrierStructural。

      NL1 边名字面量（适配器白名单除外）= 0：扫描 Projects/Plug/Agent/src 与
          Frame/CsharpPrj/Share/Frameworks/UniversalReference 的 .cs，命中关系边名集合即红。
      NL2 负对照（-NegativeControl）：合成字面量必须被同一函数判红。

    边名集合来源：reference.ushar 的 [Ref] edge= ∪ 基线 ExtraEdges（禁手写第二份边名表）。
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
$baseline = Import-GateBaseline -Name NoRelationLiteral

$allow = @($baseline.Allow | ForEach-Object { [string]$_.Value } | Where-Object { $_ })
$extraEdges = @($baseline.ExtraEdges | ForEach-Object { [string]$_.Value } | Where-Object { $_ })

$scanRoots = @(
    'Projects\Plug\Agent\src',
    'Frame\CsharpPrj\Share\Frameworks\UniversalReference'
)

function Read-RefEdges([string]$path) {
    $names = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path -LiteralPath $path)) { return $names }
    foreach ($line in (Get-Content -LiteralPath $path)) {
        if ($line -match '^\s*\[Ref\]\s+[A-Za-z0-9_\-]+\s*:.*?\bedge=([A-Za-z0-9_\-]+)') { $names.Add($Matches[1]) }
    }
    return $names
}

function Find-LiteralHits([string]$root, [string[]]$edges, [string[]]$roots, [string[]]$allowList, [string]$text = $null) {
    $hits = New-Object System.Collections.Generic.List[string]
    $needles = @($edges | Sort-Object -Unique)
    if ($null -ne $text) {
        foreach ($edge in $needles) {
            if ($text -match ('"' + [regex]::Escape($edge) + '"')) { $hits.Add("(synthetic)|$edge") }
        }
        return $hits
    }

    $allowSet = @{}
    foreach ($item in $allowList) { $allowSet[($item -replace '/', '\')] = $true }

    foreach ($rel in $roots) {
        $dir = Join-Path $root $rel
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' |
                Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' -and $_.Name -notlike '*Tests.cs' -and $_.Name -notlike '*.g.cs' })) {
            $relPath = $file.FullName.Substring($root.Length).TrimStart('\', '/')
            if ($allowSet.ContainsKey($relPath)) { continue }
            $lineNo = 0
            foreach ($line in (Get-Content -LiteralPath $file.FullName)) {
                $lineNo++
                foreach ($edge in $needles) {
                    if ($line -match ('"' + [regex]::Escape($edge) + '"')) {
                        $hits.Add(('{0}:{1}|{2}' -f $relPath, $lineNo, $edge))
                        break
                    }
                }
            }
        }
    }
    return $hits
}

$edges = @(Read-RefEdges (Join-Path $Root 'Projects\Plug\Agent\Config\nodes\foundation\reference.ushar')) + $extraEdges
$edges = @($edges | Sort-Object -Unique)

if ($edges.Count -eq 0) {
    Write-Host '[NoRelationLiteral] 边名集合为空（声明面不可解析 ⇒ 判据不可判定）' -ForegroundColor Red
    if ($Check) { exit 1 }
    exit 0
}

if ($NegativeControl) {
    $probeEdge = [string]($edges | Select-Object -First 1)
    $probe = @(Find-LiteralHits -root $Root -edges $edges -roots @() -allowList @() -text ('var e = "' + $probeEdge + '";'))
    if ($probe.Count -eq 0) {
        Write-Host '[NoRelationLiteral][NEGATIVE-CONTROL-FAILED] 合成字面量未被判红（判据空转）' -ForegroundColor Red
        exit 1
    }
    Write-Host "[NoRelationLiteral][NEGATIVE-CONTROL-OK] 合成字面量命中 $($probe.Count) 处" -ForegroundColor Green
    exit 0
}

$hits = @(Find-LiteralHits -root $Root -edges $edges -roots $scanRoots -allowList $allow)
if ($ShowDetail) { $hits | Select-Object -First 40 | ForEach-Object { Write-Host "  · $_" } }

if ($hits.Count -gt 0) {
    Write-Host "[NoRelationLiteral] 边名字面量 = $($hits.Count)（判据目标 = 0；适配器须入基线 Allow）" -ForegroundColor Red
    foreach ($hit in $hits) { Write-Host "  - $hit" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[NoRelationLiteral] 边名字面量 = 0（单源常量持有）' -ForegroundColor Green
exit 0
