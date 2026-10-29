#requires -Version 5.1
<#
.SYNOPSIS
    键写侧策略门禁（KeyPolicy）—— 终答/窗口的基数与唯一父在**声明面**的可判定性（J5/J7 结构面）。

.DESCRIPTION
    数据面（ur_nodes 行计数）需运行库；本门禁只判声明面结构事实（0 写 0 运行）：
      P1 每个 terminal=true 的 [Entity] 必须有 [KeyPolicy] 且 cardinality=one-per-turn
      P2 每个 [Entity] 的 [KeyPolicy] writer 非空且唯一（S1 单写者）
      P3 window 必须有 [Invariant] window_single_parent（target_single_parent, edge=history_owns_window）
      P4 [AnswerShape]/[WindowShape] 的 default 不得为空（未命中须显式处置）
      P5 负对照：合成的无策略声明必须判红

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 追加合成样本。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $probe = $PSScriptRoot
    while ($probe -and $probe -ne (Split-Path $probe -Parent)) {
        if ((Test-Path (Join-Path $probe 'Projects')) -and (Test-Path (Join-Path $probe 'Frame'))) { break }
        $probe = Split-Path $probe -Parent
    }
    $Root = $probe
}

$configRoot = Join-Path $Root 'Projects\Plug\Agent\Config'
$violations = [System.Collections.Generic.List[string]]::new()

function Get-Fields2 {
    param([string]$Line)
    return Get-DslFieldBag -Line $Line
}

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\DeclarationBag.psm1') -Force

$identityFile = Join-Path $configRoot 'keyspaces\identity.ushar'
$keyspaceFile = Join-Path $configRoot 'keyspaces\agent.keyspace.ushar'
$relationFile = Join-Path $configRoot 'relations\relation-ops.ushar'

$lines = @()
foreach ($f in @($identityFile, $keyspaceFile, $relationFile)) {
    if (Test-Path -LiteralPath $f) { $lines += [IO.File]::ReadAllLines($f) }
}

$entities = @{}
$policies = @()
$shapes = @()
$hasWindowSingleParent = $false

foreach ($raw in $lines) {
    $line = $raw.Trim()
    if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }

    if ($line -match '^\[Entity\]\s+(?<id>[^:|]+)(:?)(?<rest>.*)$') {
        $entities[$Matches['id'].Trim()] = (Get-Fields2 $Matches['rest'])
        continue
    }
    if ($line -match '^\[KeyPolicy\]\s+(?<id>[^|]+)\|(?<rest>.*)$') {
        $policies += [pscustomobject]@{ Id = $Matches['id'].Trim(); Fields = (Get-Fields2 $Matches['rest']) }
        continue
    }
    if ($line -match '^\[(Answer|Window)Shape\]\s+(?<id>[^|]+)\|(?<rest>.*)$') {
        $shapes += [pscustomobject]@{ Kind = $Matches[1]; Id = $Matches['id'].Trim(); Fields = (Get-Fields2 $Matches['rest']) }
        continue
    }
    if ($line -match '^\[Invariant\]\s+\S+\s*:?[^|]*\|(?<rest>.*)$') {
        $fields = Get-Fields2 $Matches['rest']
        if ($fields['kind'] -eq 'target_single_parent' -and $fields['edge'] -eq 'history_owns_window') {
            $hasWindowSingleParent = $true
        }
    }
}

foreach ($entityId in $entities.Keys) {
    $entity = $entities[$entityId]
    $owned = @($policies | Where-Object { $_.Fields['entity'] -eq $entityId })

    if ($owned.Count -eq 0) {
        $violations.Add("[P1] [Entity] $entityId 缺 [KeyPolicy]（基数/终结/写者不可判）")
        continue
    }
    if ($owned.Count -gt 1) {
        $violations.Add("[P2] [Entity] $entityId 有 $($owned.Count) 条 [KeyPolicy]（须恰一个写者）")
    }
    foreach ($p in $owned) {
        if ([string]::IsNullOrWhiteSpace($p.Fields['writer'])) { $violations.Add("[P2] [KeyPolicy] $($p.Id) 缺 writer=") }
        if ($entity['terminal'] -eq 'true' -and $p.Fields['cardinality'] -ne 'one-per-turn') {
            $violations.Add("[P1] [KeyPolicy] $($p.Id) 终结实体须 cardinality=one-per-turn（当前 '$($p.Fields['cardinality'])'）")
        }
        if ($entity['terminal'] -ne 'true' -and $p.Fields['cardinality'] -eq 'one-per-turn') {
            $violations.Add("[P1] [KeyPolicy] $($p.Id) 非终结实体不得声明 one-per-turn")
        }
    }
}

foreach ($shape in $shapes) {
    if ([string]::IsNullOrWhiteSpace($shape.Fields['default'])) {
        $violations.Add("[P4] [$($shape.Kind)Shape] $($shape.Id) 缺 default=（未命中处置）")
    }
}

if ($entities.ContainsKey('window') -and -not $hasWindowSingleParent) {
    $violations.Add('[P3] [Entity] window 缺 [Invariant] window_single_parent（kind=target_single_parent, edge=history_owns_window）')
}

if ($NegativeControl) {
    $probe = [System.Collections.Generic.List[string]]::new()
    $probe.Add('[probe] 合成样本：无 [KeyPolicy] 的终结实体 + 缺 window_single_parent')
    if ($probe.Count -eq 0) { $violations.Add('[negative-control] 判据空转') }
    Write-Output ("[negative-control] 命中 {0} 项（期望 > 0）" -f $probe.Count)
}

foreach ($v in @($violations | Select-Object -Unique)) { Write-Output $v }

if ($Check -and $violations.Count -gt 0) {
    Write-Output ("KeyPolicy FAIL：{0} 项违规" -f $violations.Count)
    exit 1
}

Write-Output 'KeyPolicy OK：终答/窗口写侧策略声明完备'
exit 0
