#requires -Version 5.1
<#
.SYNOPSIS
    关系计划守卫（RelationPlan）—— 声明面单一真源与归属边写意图的机器判据。

.DESCRIPTION
    E1 声明面存在：Config/relations/relation-ops.ushar 可读且含 [Relation]。
    E2 词表对账：[Relation].edge ⊆ reference.ushar 的 [Edge]。
    E3 归属边恰一 ensure：每条归属边恰有一个 intent=ensure 的 operation。
    E4 层级对账：hierarchy.ushar 的 contains= 边 ⊆ [Relation].edge。
    E5 负对照：合成「缺 mount」声明必须被同一判定函数命中（防恒真假绿）。
    E6 写意图完整：每条 [Relation] 必须显式 intent=ensure|remove 且声明 exists=。
    E7 C# 归属边字面量（报告态，迁移期不判红）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if (-not $Root) {
    $Root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')
}

$violations = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]

function Read-Text([string]$relative) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    return Get-Content -LiteralPath $path -Raw
}

function Get-RelationEntries([string]$text) {
    $entries = @()
    if ([string]::IsNullOrWhiteSpace($text)) { return $entries }
    foreach ($line in ($text -split "`n")) {
        if ($line -notmatch '^\s*\[Relation\]\s+([A-Za-z0-9_\-]+)\s*:') { continue }
        $id = $Matches[1]
        $fields = @{}
        foreach ($m in [regex]::Matches($line, '([A-Za-z_]+)\s*=\s*([^|]+)')) {
            $fields[$m.Groups[1].Value] = $m.Groups[2].Value.Trim()
        }
        $entries += [pscustomobject]@{ Id = $id; Edge = $fields['edge']; Intent = $fields['intent']; Exists = $fields['exists'] }
    }
    return $entries
}

function Get-EdgeNames([string]$text) {
    $names = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($text)) { return $names }
    foreach ($line in ($text -split "`n")) {
        if ($line -match '^\s*\[Edge\]\s+([A-Za-z0-9_\-]+)\s*:') { $names.Add($Matches[1]) }
    }
    return $names
}

function Get-ContainsEdges([string]$text) {
    $names = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($text)) { return $names }
    foreach ($m in [regex]::Matches($text, 'contains=([A-Za-z0-9_\-]+):([A-Za-z0-9_\-]+)')) {
        $names.Add($m.Groups[2].Value)
    }
    return $names
}


$ownershipEdges = @('cross_db_link', 'history_owns_task', 'session_owns', 'task_owns_task')

function Test-RelationSet($relations, $edgeNames, $containsEdges) {
    $problems = New-Object System.Collections.Generic.List[string]
    if ($relations.Count -eq 0) { $problems.Add('E1 relation-ops.ushar 无 [Relation] 声明'); return $problems }

    $edgeSet = @{}
    foreach ($edge in $edgeNames) { $edgeSet[$edge] = $true }

    foreach ($rel in $relations) {
        if ([string]::IsNullOrWhiteSpace($rel.Edge)) { $problems.Add("E2 $($rel.Id) 缺 edge=") ; continue }
        if (-not $edgeSet.ContainsKey($rel.Edge)) { $problems.Add("E2 $($rel.Id) edge=$($rel.Edge) 未在 [Edge] 声明") }
        if ($rel.Intent -notin @('ensure', 'remove')) { $problems.Add("E6 $($rel.Id) intent 缺失或非法（须 ensure|remove）") }
        if ($null -eq $rel.Exists) { $problems.Add("E6 $($rel.Id) 缺 exists=（须显式声明探针范围）") }
    }

    foreach ($edge in $ownershipEdges) {
        $ensure = @($relations | Where-Object { $_.Edge -eq $edge -and $_.Intent -eq 'ensure' })
        if ($ensure.Count -ne 1) { $problems.Add("E3 归属边 $edge 须恰一 ensure operation（实测 $($ensure.Count)）") }
    }

    foreach ($edge in $containsEdges) {
        if (-not ($relations | Where-Object { $_.Edge -eq $edge })) {
            $problems.Add("E4 hierarchy.ushar contains=$edge 未在 [Relation] 声明")
        }
    }

    return $problems
}

$relationText = Read-Text 'Projects\Plug\Agent\Config\relations\relation-ops.ushar'
$referenceText = Read-Text 'Projects\Plug\Agent\Config\nodes\reference.ushar'
$hierarchyText = Read-Text 'Projects\Plug\Agent\Config\hierarchy.ushar'

if ([string]::IsNullOrWhiteSpace($relationText)) { $violations.Add('E1 relation-ops.ushar 缺失') }
if ([string]::IsNullOrWhiteSpace($referenceText)) { $violations.Add('E2 reference.ushar 缺失') }
if ([string]::IsNullOrWhiteSpace($hierarchyText)) { $violations.Add('E4 hierarchy.ushar 缺失') }

if ($violations.Count -eq 0) {
    $relations = @(Get-RelationEntries $relationText)
    $edgeNames = @(Get-EdgeNames $referenceText)
    $containsEdges = @(Get-ContainsEdges $hierarchyText)

    foreach ($problem in (Test-RelationSet $relations $edgeNames $containsEdges)) { $violations.Add($problem) }

    if ($ShowDetail) {
        $details.Add("E1 [Relation]=$($relations.Count) / [Edge]=$($edgeNames.Count) / contains=$($containsEdges.Count)")
    }

    # E5 负对照：删掉 cross_db_link 的 ensure 后同一判定函数必须报 E3
    $synthetic = @($relations | Where-Object { -not ($_.Edge -eq 'cross_db_link' -and $_.Intent -eq 'ensure') })
    $negative = Test-RelationSet $synthetic $edgeNames $containsEdges
    if (-not ($negative | Where-Object { $_ -like 'E3 *cross_db_link*' })) {
        $violations.Add('E5 负对照失效（缺 ensure 未被判红）')
    } elseif ($ShowDetail) { $details.Add('E5 负对照命中 E3 OK') }

    # E7 报告态：C# 归属边字面量（迁移期不判红；D 波后目标 0）
    $literalFiles = 0
    foreach ($src in @((Join-Path $Root 'Frame\CsharpPrj'), (Join-Path $Root 'Projects\Plug\Agent\src'))) {
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $hits = Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\Tests\\' -and $_.Name -notlike '*Tests.cs' -and $_.Name -notlike '*.generated.*' } |
            Select-String -Pattern '"(cross_db_link|history_owns_task|session_owns|task_owns_task)"' -ErrorAction SilentlyContinue
        $literalFiles += @($hits | ForEach-Object { $_.Path } | Sort-Object -Unique).Count
    }
    $details.Add("E7 报告态：C# 归属边字面量文件=$literalFiles（D 波完成后目标 0）")
}

if ($ShowDetail) { foreach ($line in $details) { Write-Host "  $line" } }

if ($violations.Count -eq 0) {
    Write-Host 'RelationPlan: PASS'
    exit 0
}

foreach ($violation in $violations) { Write-Host "  VIOLATION: $violation" }
if ($Check) { exit 1 }
exit 0
