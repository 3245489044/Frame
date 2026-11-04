#requires -Version 5.1
<#
.SYNOPSIS
    面白名单门禁（FaceWhitelist）—— 面选择必须肯定式且可落地。

.DESCRIPTION
    判据载体 = 声明面静态结构（`Config/graph.ushar` 的 [Product]/[Face] 行）；执行者叙述不参与。

      FW1 Face 三要素齐备：product= 存在、owner= 非空、channel= 非空（任缺 ⇒ 该面永不产出）
      FW2 面引用的产品已声明（product= ∈ [Product] 集合）
      FW3 面字段 ⊆ 产品出字段（fields= 未声明 ⇒ 取全量，不判）
      FW4 负对照（-NegativeControl）：合成样本（缺 channel / 悬空 product）必须判红

    模式：默认报告；-Check 违规 ⇒ 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name FaceWhitelist
$declarationRel = [string]$baseline.ProductGraph.Value
$declarationPath = Join-Path $Root $declarationRel
if (-not (Test-Path -LiteralPath $declarationPath)) {
    throw "FaceWhitelist：声明面不存在 $declarationRel（判据不可判定）"
}

function Read-Declared([string]$Text, [string]$Prefix) {
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($line in ($Text -split "`r?`n")) {
        $pattern = '^\s*' + [regex]::Escape($Prefix) + '\s+([A-Za-z0-9_.\-]+)\s*:'
        if ($line -notmatch $pattern) { continue }
        $name = $Matches[1]
        $fields = @{}
        foreach ($segment in ($line -split '\|')) {
            if ($segment -match '^\s*([A-Za-z0-9_\-]+)\s*=\s*(.*)$') {
                $fields[$Matches[1].ToLowerInvariant()] = $Matches[2].Trim()
            }
        }
        $items.Add([pscustomobject]@{ Name = $name; Fields = $fields })
    }
    return $items
}

function Split-Csv([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return @() }
    return @($Value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Test-Faces([string]$Text) {
    $problems = [System.Collections.Generic.List[string]]::new()
    $products = @{}
    foreach ($product in (Read-Declared -Text $Text -Prefix '[Product]')) {
        $products[$product.Name] = Split-Csv ([string]$product.Fields['out'])
    }

    foreach ($face in (Read-Declared -Text $Text -Prefix '[Face]')) {
        $product = [string]$face.Fields['product']
        $owners = Split-Csv ([string]$face.Fields['owner'])
        $channels = Split-Csv ([string]$face.Fields['channel'])
        $fields = Split-Csv ([string]$face.Fields['fields'])

        if ([string]::IsNullOrWhiteSpace($product)) { $problems.Add("FW1 $($face.Name): 缺 product=") }
        elseif (-not $products.ContainsKey($product)) { $problems.Add("FW2 $($face.Name): product=$product 未声明") }
        if ($owners.Count -eq 0) { $problems.Add("FW1 $($face.Name): owner= 为空") }
        if ($channels.Count -eq 0) { $problems.Add("FW1 $($face.Name): channel= 为空（否定式残留）") }

        if ($fields.Count -gt 0 -and $products.ContainsKey($product)) {
            $out = @($products[$product])
            foreach ($field in $fields) {
                if ($out -notcontains $field) { $problems.Add("FW3 $($face.Name): fields 越权 $field ∉ $product.out") }
            }
        }
    }

    return $problems
}

if ($NegativeControl) {
    $sample = @'
[Product] p.ok: x | level=node | out=a,b
[Face] f.bad: x | product=p.ok | owner=o
[Face] f.dangling: x | product=p.missing | owner=o | channel=Operation
[Face] f.over: x | product=p.ok | owner=o | channel=Operation | fields=a,zzz
'@
    $problems = @(Test-Faces -Text $sample)
    if ($problems.Count -lt 3) {
        Write-Host "[FaceWhitelist][NEGATIVE-CONTROL-FAILED] 合成样本未全部判红（命中 $($problems.Count)）" -ForegroundColor Red
        exit 1
    }
    Write-Host "[FaceWhitelist][NEGATIVE-CONTROL-OK] 合成样本命中 $($problems.Count) 项" -ForegroundColor Green
    exit 0
}

$text = Get-Content -LiteralPath $declarationPath -Raw
$violations = @(Test-Faces -Text $text)

if ($violations.Count -gt 0) {
    Write-Host "[FaceWhitelist] 违规 = $($violations.Count)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[FaceWhitelist] 面白名单齐备（product/owner/channel + 字段闭合）' -ForegroundColor Green
exit 0
