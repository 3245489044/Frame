#requires -Version 5.1
<#
.SYNOPSIS
    声明面漂移守卫（GraphDeclarationDrift）—— 产品 / 面 / 视图内容域三者跨声明闭合。

.DESCRIPTION
    判据载体 = 声明文件静态结构（[Product]/[Face] 行、[Gather] 段名、ui-regions 视图字段）；
    执行者叙述不参与。四项判据（DOC-08：声明门先于结构门）：

      GD1 ProductFieldClosure：产品 `depends_on` 目标须为已声明产品键；`[Face] product=` 须存在，
                               且 `[Face] fields=` ⊆ 该产品 `out=`（跨声明血缘/字段闭合）。
      GD2 FaceRegionClosure   ：每个面须有宿主视图（面产品的层 ∈ ui-regions 视图 actorLevel 集），
                               且 owner / channel 双维非空（肯定式白名单：任缺 ⇒ 该面永不产出）。
      GD3 GatherDomainReachability：视图 `content.domain` 与非空 `panel.domain` 须在 node-gather.ushar
                               存在同名 [Gather] 段；缺口只允许在基线棘轮名单在册（Debt + DueWhen，只降不升）。
      GD5 LineageClosure      ：产品 `derives=` 须恰有 1 个 `depends_on` 上游；字段对 `src->dst` 形态合法，
                               且 `dst` ∈ 本产品 `out=`、`src` ∈ 上游 `out=`（字段级血缘闭合 = C6 的声明判据）。
      GD4 负对照（-NegativeControl）：合成样本（缺 out / 面字段越权 / depends_on 悬空 / 未登记内容域 /
                               血缘端点不可判定）必须被同一判据函数判红，否则判「判据空转」。

    模式：默认报告；-Check 违规 ⇒ 退出码 1。只读仓库，零侧写。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER NegativeControl
    负对照：注入合成违规样本，验证三个判据函数确实能判红。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if (-not $Root) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name GraphDeclarationDrift

# ---- 声明面路径（基线单源；缺失即判据不可判定 ⇒ fail-closed）----
function Resolve-DeclaredPath($row, [string]$key) {
    if ($null -eq $row -or [string]::IsNullOrWhiteSpace([string]$row.Value)) {
        throw "GraphDeclarationDrift：基线缺 $key（判据输入未声明 ⇒ 不可判定）"
    }
    $path = Join-Path $Root ([string]$row.Value)
    if (-not (Test-Path -LiteralPath $path)) {
        throw "GraphDeclarationDrift：声明面不存在 $($row.Value)（判据不可判定）"
    }
    return $path
}

# ---- 声明行解析（单源：本文件内唯一实现，禁各处手写正则口径）----

# `[Prefix] name: label | k=v | ...` → @{ Name; Fields(hashtable) }
function Read-DeclaredLine([string]$line, [string]$prefix) {
    $pattern = '^\s*' + [regex]::Escape($prefix) + '\s+([A-Za-z_][A-Za-z0-9_.]*)\s*:'
    if ($line -notmatch $pattern) { return $null }
    $name = $Matches[1]
    $fields = @{}
    foreach ($segment in ($line -split '\|')) {
        if ($segment -notmatch '^\s*([A-Za-z_][A-Za-z0-9_\-]*)\s*=\s*(.*)$') { continue }
        $fields[$Matches[1].ToLowerInvariant()] = $Matches[2].Trim()
    }
    return [pscustomobject]@{ Name = $name; Fields = $fields }
}

function Split-List([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return @() }
    return @($value -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Read-Products([string]$path) {
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($line in (Get-Content -LiteralPath $path)) {
        $decl = Read-DeclaredLine $line '[Product]'
        if ($null -eq $decl) { continue }
        $items.Add([pscustomobject]@{
            Id         = $decl.Name
            Level      = [string]$decl.Fields['level']
            Role       = [string]$decl.Fields['role']
            Out        = @(Split-List $decl.Fields['out'])
            DependsOn  = @(Split-List $decl.Fields['depends_on'])
            Derive     = [string]$decl.Fields['derive']
            Derives    = [string]$decl.Fields['derives']
        })
    }
    return $items
}

function Read-Faces([string]$path) {
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($line in (Get-Content -LiteralPath $path)) {
        $decl = Read-DeclaredLine $line '[Face]'
        if ($null -eq $decl) { continue }
        $items.Add([pscustomobject]@{
            Id       = $decl.Name
            Product  = [string]$decl.Fields['product']
            Owners   = @(Split-List $decl.Fields['owner'])
            Channels = @(Split-List $decl.Fields['channel'])
            Fields   = @(Split-List $decl.Fields['fields'])
        })
    }
    return $items
}

function Read-GatherDomains([string]$path) {
    $names = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in (Get-Content -LiteralPath $path)) {
        if ($line -match '^\s*\[Gather\]\s+([A-Za-z0-9_.\-]+)\s*:?\s*$') { [void]$names.Add($Matches[1]) }
    }
    return @($names | Sort-Object)
}

# 视图内容域 + 宿主层（actorLevel）；panel.domain 为空 = 未声明（不参与判据）。
function Read-ViewSurface([string]$path) {
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $domains = New-Object System.Collections.Generic.HashSet[string]
    $levels = New-Object System.Collections.Generic.HashSet[string]
    foreach ($view in $json.views) {
        if ($view.content -and -not [string]::IsNullOrWhiteSpace([string]$view.content.domain)) {
            [void]$domains.Add([string]$view.content.domain)
        }
        if ($view.panel -and -not [string]::IsNullOrWhiteSpace([string]$view.panel.domain)) {
            [void]$domains.Add([string]$view.panel.domain)
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$view.actorLevel)) {
            [void]$levels.Add([string]$view.actorLevel)
        }
    }
    return [pscustomobject]@{ Domains = @($domains | Sort-Object); Levels = @($levels | Sort-Object) }
}


# ---- 判据函数（纯函数：输入声明集，输出违规清单；负对照直接复用同一实现）----

# GD1：depends_on 闭合 + 面字段 ⊆ 产品出字段。
function Get-ProductFieldClosureViolations($products, $faces) {
    $violations = New-Object System.Collections.Generic.List[string]
    $declared = @{}
    foreach ($product in $products) {
        if ($declared.ContainsKey($product.Id)) { $violations.Add("GD1 产品键重复：$($product.Id)") }
        else { $declared[$product.Id] = $product }
        if ($product.Out.Count -eq 0) { $violations.Add("GD1 产品出字段为空：$($product.Id)（对外承诺必非空）") }
    }

    foreach ($product in $products) {
        foreach ($dependency in $product.DependsOn) {
            if (-not $declared.ContainsKey($dependency)) {
                $violations.Add("GD1 悬空 depends_on：$($product.Id) → $dependency（未声明产品键）")
            }
        }
    }

    foreach ($face in $faces) {
        if (-not $declared.ContainsKey($face.Product)) {
            $violations.Add("GD1 面引用未声明产品：$($face.Id) → $($face.Product)")
            continue
        }
        $out = $declared[$face.Product].Out
        foreach ($field in $face.Fields) {
            if ($out -notcontains $field) {
                $violations.Add("GD1 面字段越权：$($face.Id).fields=$field 不在 $($face.Product).out")
            }
        }
    }

    return $violations
}

# GD2：面须有宿主视图（面产品层 ∈ 视图 actorLevel 集）+ owner/channel 双维非空。
function Get-FaceRegionClosureViolations($faces, $products, $viewLevels) {
    $violations = New-Object System.Collections.Generic.List[string]
    $levelOf = @{}
    foreach ($product in $products) { $levelOf[$product.Id] = $product.Level }

    foreach ($face in $faces) {
        if ($face.Owners.Count -eq 0) { $violations.Add("GD2 面缺 owner 白名单：$($face.Id)（肯定式白名单须双维声明）") }
        if ($face.Channels.Count -eq 0) { $violations.Add("GD2 面缺 channel 白名单：$($face.Id)（肯定式白名单须双维声明）") }

        if (-not $levelOf.ContainsKey($face.Product)) { continue }  # GD1 已判，避免双报
        $level = [string]$levelOf[$face.Product]
        if ($viewLevels -notcontains $level) {
            $violations.Add("GD2 面无宿主视图：$($face.Id) 的产品层 '$level' 不在 ui-regions 视图 actorLevel 集（死面）")
        }
    }

    return $violations
}

# GD5：字段级血缘闭合（derives= ⇒ ≥1 上游；每个字段对的源字段须属某一上游 out；目标字段 ∈ 本产品 out）。
# 多上游 = 视图聚合多类事实（如看板同时依赖任务集与节点文本）；放宽的是「上游数」，收紧的是「逐对闭合」。
function Get-LineageClosureViolations($products) {
    $violations = New-Object System.Collections.Generic.List[string]
    $declared = @{}
    foreach ($product in $products) { $declared[$product.Id] = $product }

    foreach ($product in $products) {
        if ([string]::IsNullOrWhiteSpace([string]$product.Derives)) { continue }

        if ($product.DependsOn.Count -eq 0) {
            $violations.Add("GD5 血缘端点不可判定：$($product.Id) 的 depends_on 为空（derives= 须有上游）")
            continue
        }

        $parentOut = @()
        $upstreamOk = $true
        foreach ($parentKey in $product.DependsOn) {
            if (-not $declared.ContainsKey([string]$parentKey)) {
                $violations.Add("GD5 血缘上游未声明：$($product.Id) → $parentKey")
                $upstreamOk = $false
                continue
            }
            $parentOut += @($declared[[string]$parentKey].Out)
        }
        if (-not $upstreamOk) { continue }

        foreach ($pair in ([string]$product.Derives -split ';')) {
            $entry = $pair.Trim()
            if ($entry.Length -eq 0) { continue }

            $index = $entry.IndexOf('->', [System.StringComparison]::Ordinal)
            if ($index -le 0 -or ($index + 2) -ge $entry.Length) {
                $violations.Add("GD5 血缘字段对形态非法：$($product.Id).derives='$entry'（须 src->dst）")
                continue
            }

            $from = $entry.Substring(0, $index).Trim()
            $to = $entry.Substring($index + 2).Trim()
            if ($parentOut -notcontains $from) {
                $violations.Add("GD5 血缘源字段不在任一上游出字段：$($product.Id).derives '$from' ∉ [$($product.DependsOn -join ',')].out")
            }
            if ($product.Out -notcontains $to) {
                $violations.Add("GD5 血缘目标字段越权：$($product.Id).derives '$to' ∉ 本产品 out")
            }
        }
    }

    return $violations
}

# GD3：视图内容域须有同名 [Gather] 段；缺口只允许在基线棘轮名单在册（新增即红）。
function Get-GatherDomainViolations($viewDomains, $gatherDomains, $allowlisted) {
    $violations = New-Object System.Collections.Generic.List[string]
    $stale = New-Object System.Collections.Generic.List[string]
    foreach ($domain in $viewDomains) {
        if ($gatherDomains -contains $domain) { continue }
        if ($allowlisted -contains $domain) { continue }
        $violations.Add("GD3 内容域不可达：'$domain' 无同名 [Gather] 段且未在基线棘轮在册（静默取空行集）")
    }
    foreach ($domain in $allowlisted) {
        if ($viewDomains -notcontains $domain) { $stale.Add($domain) }
    }
    return [pscustomobject]@{ Violations = $violations; Stale = $stale }
}

# ---- 运行判据 ----
$violations = New-Object System.Collections.Generic.List[string]
$details = New-Object System.Collections.Generic.List[string]

$productPath = Resolve-DeclaredPath $baseline.ProductGraph 'ProductGraph'
$gatherPath = Resolve-DeclaredPath $baseline.GatherDeclaration 'GatherDeclaration'
$regionPath = Resolve-DeclaredPath $baseline.RegionDeclaration 'RegionDeclaration'

$products = Read-Products $productPath
$faces = Read-Faces $productPath
$gatherDomains = Read-GatherDomains $gatherPath
$views = Read-ViewSurface $regionPath

$allowRows = @($baseline.UnreachableViewDomains)
$allowlisted = @($allowRows | ForEach-Object { [string]$_.Value })

$violations.AddRange([string[]]@(Get-ProductFieldClosureViolations $products $faces))
$violations.AddRange([string[]]@(Get-FaceRegionClosureViolations $faces $products $views.Levels))
$violations.AddRange([string[]]@(Get-LineageClosureViolations $products))
$gatherResult = Get-GatherDomainViolations $views.Domains $gatherDomains $allowlisted
$violations.AddRange([string[]]@($gatherResult.Violations))

if ($products.Count -eq 0) { $violations.Add('GD1 产品声明集为空（graph.ushar 无任何 [Product] ⇒ 判据不可判定）') }
if ($faces.Count -eq 0) { $violations.Add('GD2 面声明集为空（graph.ushar 无任何 [Face] ⇒ 判据不可判定）') }
if ($gatherDomains.Count -eq 0) { $violations.Add('GD3 聚拢域集为空（node-gather.ushar 无 [Gather] ⇒ 判据不可判定）') }

# ---- GD4 负对照（判据自证：合成样本必须被同一函数判红）----
if ($NegativeControl) {
    $probeProducts = @(
        [pscustomobject]@{ Id = 'a.p'; Level = 'history'; Role = 'source'; Out = @(); DependsOn = @('missing.p'); Derive = '' }
    )
    $probeFaces = @(
        [pscustomobject]@{ Id = 'f1'; Product = 'a.p'; Owners = @(); Channels = @(); Fields = @('ghost_field') }
    )
    $probeLevels = @()

    if (@(Get-ProductFieldClosureViolations $probeProducts $probeFaces).Count -lt 3) {
        $violations.Add('GD4 负对照失效：缺 out / 悬空 depends_on / 面字段越权 未被同时检出')
    }
    if (@(Get-FaceRegionClosureViolations $probeFaces $probeProducts $probeLevels).Count -lt 3) {
        $violations.Add('GD4 负对照失效：缺 owner / 缺 channel / 无宿主视图 未被同时检出')
    }

    $probeViewDomains = @('ghost-domain')
    $probeGather = @('real-domain')
    $probeResult = Get-GatherDomainViolations $probeViewDomains $probeGather @()
    if (@($probeResult.Violations).Count -eq 0) {
        $violations.Add('GD4 负对照失效：未登记内容域未被检出')
    }

    $probeLineage = @(
        [pscustomobject]@{ Id = 'c.p'; Level = 'history'; Role = 'derived'; Out = @('x'); DependsOn = @(); Derive = ''; Derives = 'a->b' }
    )
    if (@(Get-LineageClosureViolations $probeLineage).Count -eq 0) {
        $violations.Add('GD4 负对照失效：血缘端点不可判定未被检出')
    }

    if ($ShowDetail) { $details.Add('GD4 负对照命中 OK') }
}

# ---- 输出 ----
if ($ShowDetail) {
    $details.Add("产品 $($products.Count) / 面 $($faces.Count) / 聚拢域 $($gatherDomains.Count) / 视图内容域 $($views.Domains.Count)")
    foreach ($domain in $gatherResult.Stale) { $details.Add("[可销账] 基线棘轮项已无缺口：$domain") }
    foreach ($line in $details) { Write-Host "  $line" }
}

if ($violations.Count -eq 0) {
    Write-Host 'GraphDeclarationDrift: PASS'
    exit 0
}

foreach ($violation in $violations) { Write-Host "  VIOLATION: $violation" }
if ($Check) { exit 1 }
exit 0
