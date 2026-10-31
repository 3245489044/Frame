#requires -Version 5.1
<#
.SYNOPSIS
    聚拢声明守卫（GatherSpec）—— 声明面合格性 + 末路完备 + 装配期闭合（G1）。
.DESCRIPTION
    判据（每条可机械复算）：
      S1 声明面存在且 schema = gather-spec-v1。
      S2 视图非空；每视图有 stages；stage 名唯一。
      S3 stage.needs ⊆ 视图 needs（映射闭合：声明了却没 need ⇒ 装配期必炸）。
      S4 stage.deps 指向存在的 stage 且无环（拓扑可解）。
      S5 produces 名唯一；crit ∈ {micro,macro}；crit=macro 必带 recompute。
      S6 声明面禁过程语义键（step/order/sequence/then/next）。
      S7 正/负对照：缺 need / 成环 / 出现 step 键 三类合成样本必须被判据捕获。
      S8 命名合格性：视图名 / stage 名 / need 域名 / produce 名须匹配 ^[a-z][a-z0-9._-]*$
         （单源 = GatherSchema.NamePattern；与实现侧声明同规，防两侧同名同错即通过）。
    模式：默认报告（不设退出码）；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()
$forbidden = @('step', 'order', 'sequence', 'then', 'next')
$namePattern = '^[a-z][a-z0-9._-]*$'

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢声明守卫（GatherSpec）'
Write-Host '============================================================'

<# 判据核心（纯函数；负对照直接复用 ⇒ 判据不可被「恒真」绕过）。 #>
function Get-GatherSpecIssues
{
    param($Spec)

    $issues = [System.Collections.Generic.List[string]]::new()

    if ($null -eq $Spec -or -not ($Spec.PSObject.Properties.Name -contains 'schema')) {
        $issues.Add('[S1] 声明缺 schema'); return , $issues
    }
    if ([string]$Spec.schema -ne 'gather-spec-v1') {
        $issues.Add("[S1] schema 非法：'$($Spec.schema)'（期望 gather-spec-v1）")
    }
    if ($null -eq $Spec.views -or -not ($Spec.views.PSObject.Properties.Name)) {
        $issues.Add('[S2] views 为空（判据无标的）'); return , $issues
    }

    foreach ($viewProp in $Spec.views.PSObject.Properties) {
        $viewKey = $viewProp.Name
        $view = $viewProp.Value

        if ($viewKey -cnotmatch $namePattern) {
            $issues.Add("[S8] 视图名非法：'$viewKey'（须匹配 $namePattern）")
        }

        $stages = @($view.stages)
        if ($stages.Count -eq 0) { $issues.Add("[S2] 视图 '$viewKey' 无 stages") }

        $needDomains = @($view.needs | ForEach-Object { [string]$_.domain })
        if ($needDomains.Count -eq 0) { $issues.Add("[S3] 视图 '$viewKey' 无 needs") }
        foreach ($domain in $needDomains) {
            if ($domain -cnotmatch $namePattern) {
                $issues.Add("[S8] 视图 '$viewKey' need 域名非法：'$domain'（须匹配 $namePattern）")
            }
        }

        $stageNames = @($stages | ForEach-Object { [string]$_.name })
        $dupes = @($stageNames | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
        foreach ($dupe in $dupes) { $issues.Add("[S2] 视图 '$viewKey' stage 重名：'$dupe'") }
        foreach ($name in $stageNames) {
            if ($name -cnotmatch $namePattern) {
                $issues.Add("[S8] 视图 '$viewKey' stage 名非法：'$name'（须匹配 $namePattern）")
            }
        }

        foreach ($stage in $stages) {
            foreach ($need in @($stage.needs)) {
                if ($needDomains -notcontains [string]$need) {
                    $issues.Add("[S3] 视图 '$viewKey' 阶段 '$($stage.name)' 引用未声明 need '$need'")
                }
            }
            foreach ($dep in @($stage.deps)) {
                if ($stageNames -notcontains [string]$dep) {
                    $issues.Add("[S4] 视图 '$viewKey' 阶段 '$($stage.name)' 依赖不存在阶段 '$dep'")
                }
            }
        }

        # 环检测：DFS（动态作用域共享 $edges/$visiting/$done）。
        $edges = @{}
        foreach ($stage in $stages) { $edges[[string]$stage.name] = @($stage.deps | ForEach-Object { [string]$_ }) }
        $visiting = [System.Collections.Generic.HashSet[string]]::new()
        $done = [System.Collections.Generic.HashSet[string]]::new()
        function Test-GatherCycle([string]$Name) {
            if ($done.Contains($Name)) { return $false }
            if (-not $visiting.Add($Name)) { return $true }
            if ($edges.ContainsKey($Name)) {
                foreach ($dep in $edges[$Name]) { if (Test-GatherCycle $dep) { return $true } }
            }
            [void]$visiting.Remove($Name); [void]$done.Add($Name); return $false
        }
        $cycle = $false
        foreach ($name in $stageNames) { if (Test-GatherCycle $name) { $cycle = $true; break } }
        if ($cycle) { $issues.Add("[S4] 视图 '$viewKey' 的阶段图存在环") }

        foreach ($produce in @($view.produces)) {
            $crit = [string]$produce.crit
            $produceName = [string]$produce.name
            if ($produceName -cnotmatch $namePattern) {
                $issues.Add("[S8] 视图 '$viewKey' produce 名非法：'$produceName'（须匹配 $namePattern）")
            }
            if ($crit -notin @('micro', 'macro')) {
                $issues.Add("[S5] 视图 '$viewKey' 产物 '$($produce.name)' 的 crit='$crit' 非法")
            }
            if ($crit -eq 'macro' -and [string]::IsNullOrWhiteSpace([string]$produce.recompute)) {
                $issues.Add("[S5] 视图 '$viewKey' 产物 '$($produce.name)' 为 crit:macro 但缺 recompute")
            }
        }
    }

    return , $issues
}

<# 过程语义键扫描（递归）。 #>
function Get-ForbiddenKeyHits
{
    param($Node, [string]$Path)
    $hits = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Node) { return , $hits }

    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($prop in $Node.PSObject.Properties) {
            if ($forbidden -contains $prop.Name) { $hits.Add("$Path.$($prop.Name)") }
            foreach ($h in (Get-ForbiddenKeyHits -Node $prop.Value -Path "$Path.$($prop.Name)")) { $hits.Add($h) }
        }
    }
    elseif ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        $i = 0
        foreach ($item in $Node) {
            foreach ($h in (Get-ForbiddenKeyHits -Node $item -Path "$Path[$i]")) { $hits.Add($h) }
            $i++
        }
    }
    return , $hits
}

function Add-OrReplaceItem
{
    param([System.Collections.IList]$Items, $Item, [string]$KeyName)
    for ($i = 0; $i -lt $Items.Count; $i++) {
        if ([string]$Items[$i].$KeyName -ceq [string]$Item.$KeyName) { $Items[$i] = $Item; return }
    }
    [void]$Items.Add($Item)
}

function Resolve-ViewEffective
{
    param([string]$Key, $Views, [hashtable]$Cache, [System.Collections.Generic.HashSet[string]]$Visiting,
        [System.Collections.Generic.List[string]]$Issues)

    if ($Cache.ContainsKey($Key)) { return $Cache[$Key] }
    $view = $Views.$Key
    if ($null -eq $view) { return $null }
    if (-not $Visiting.Add($Key)) {
        $Issues.Add("[S9] 视图 '$Key' 的 extends 链存在环")
        return $null
    }

    $stages = [System.Collections.Generic.List[object]]::new()
    $needs = [System.Collections.Generic.List[object]]::new()
    $produces = [System.Collections.Generic.List[object]]::new()

    $parentKey = [string]$view.extends
    if (-not [string]::IsNullOrWhiteSpace($parentKey)) {
        $parent = Resolve-ViewEffective -Key $parentKey -Views $Views -Cache $Cache -Visiting $Visiting -Issues $Issues
        if ($null -eq $parent) {
            $Issues.Add("[S9] 视图 '$Key' extends 未声明视图 '$parentKey'")
        }
        else {
            foreach ($s in @($parent.stages)) { Add-OrReplaceItem -Items $stages -Item $s -KeyName 'name' }
            foreach ($n in @($parent.needs)) { Add-OrReplaceItem -Items $needs -Item $n -KeyName 'domain' }
            foreach ($p in @($parent.produces)) { Add-OrReplaceItem -Items $produces -Item $p -KeyName 'name' }
        }
    }

    foreach ($s in @($view.stages)) { if ($null -ne $s) { Add-OrReplaceItem -Items $stages -Item $s -KeyName 'name' } }
    foreach ($n in @($view.needs)) { if ($null -ne $n) { Add-OrReplaceItem -Items $needs -Item $n -KeyName 'domain' } }
    foreach ($p in @($view.produces)) { if ($null -ne $p) { Add-OrReplaceItem -Items $produces -Item $p -KeyName 'name' } }

    $excludeStages = @($view.excludeStages | ForEach-Object { [string]$_ } | Where-Object { $_ })
    $excludeNeeds = @($view.excludeNeeds | ForEach-Object { [string]$_ } | Where-Object { $_ })
    $excludeProduces = @($view.excludeProduces | ForEach-Object { [string]$_ } | Where-Object { $_ })

    foreach ($name in $excludeStages) {
        if (-not ($stages | Where-Object { [string]$_.name -ceq $name })) {
            $Issues.Add("[S10] 视图 '$Key' excludeStages 指向基视图不存在的阶段 '$name'")
        }
    }
    foreach ($domain in $excludeNeeds) {
        if (-not ($needs | Where-Object { [string]$_.domain -ceq $domain })) {
            $Issues.Add("[S10] 视图 '$Key' excludeNeeds 指向基视图不存在的 need '$domain'")
        }
    }
    foreach ($name in $excludeProduces) {
        if (-not ($produces | Where-Object { [string]$_.name -ceq $name })) {
            $Issues.Add("[S10] 视图 '$Key' excludeProduces 指向基视图不存在的产物 '$name'")
        }
    }

    $effective = [pscustomobject]@{
        stages   = @($stages | Where-Object { $excludeStages -notcontains [string]$_.name })
        needs    = @($needs | Where-Object { $excludeNeeds -notcontains [string]$_.domain })
        produces = @($produces | Where-Object { $excludeProduces -notcontains [string]$_.name })
    }

    [void]$Visiting.Remove($Key)
    $Cache[$Key] = $effective
    return $effective
}

<# 组合折叠（S9/S10）→ 返回 effective spec（视图名 → 展开后视图）。 #>
function Get-EffectiveSpec
{
    param($Spec, [System.Collections.Generic.List[string]]$Issues)

    if ($null -eq $Spec -or $null -eq $Spec.views) { return $Spec }
    $cache = @{}
    $resolved = [ordered]@{}
    foreach ($prop in $Spec.views.PSObject.Properties) {
        $effective = Resolve-ViewEffective -Key $prop.Name -Views $Spec.views -Cache $cache `
            -Visiting ([System.Collections.Generic.HashSet[string]]::new()) -Issues $Issues
        if ($null -ne $effective) { $resolved[$prop.Name] = $effective }
    }

    return [pscustomobject]@{ schema = $Spec.schema; views = [pscustomobject]$resolved }
}

$declDir = Join-Path $Root 'Projects\Plug\Agent\Config\gather'
$files = @(Get-ChildItem -LiteralPath $declDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    $violations.Add("[S1] 聚拢声明缺失：$declDir\*.json（缺失即判红，禁回退 C# 常量）")
}

$totalViews = 0
foreach ($file in $files) {
    try { $spec = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json }
    catch { $violations.Add("[S1] $($file.Name) JSON 解析失败：$($_.Exception.Message)"); continue }

    # S9/S10 组合折叠 → S11 对 effective 复跑合格性（raw 视图只有 extends，直接校验会假红）。
    $compositionIssues = [System.Collections.Generic.List[string]]::new()
    $effectiveSpec = Get-EffectiveSpec -Spec $spec -Issues $compositionIssues
    foreach ($issue in $compositionIssues) { $violations.Add("$($file.Name) $issue") }
    foreach ($issue in (Get-GatherSpecIssues -Spec $effectiveSpec)) { $violations.Add("$($file.Name) $issue") }
    foreach ($hit in (Get-ForbiddenKeyHits -Node $spec -Path '$')) {
        $violations.Add("[S6] $($file.Name) 含过程语义键：$hit")
    }
    $totalViews += @($spec.views.PSObject.Properties).Count
}

# ---------------- 负对照（防假绿） ----------------
$probeMissing = '{"schema":"gather-spec-v1","views":{"v":{"stages":[{"name":"s","needs":["ghost"],"deps":[]}],"needs":[{"domain":"a","scope":"x"}],"produces":[]}}}' | ConvertFrom-Json
if (@(Get-GatherSpecIssues -Spec $probeMissing).Count -eq 0) {
    $violations.Add('[S7] 负对照失效：缺 need 引用的样本未被捕获')
}

$probeCycle = '{"schema":"gather-spec-v1","views":{"v":{"stages":[{"name":"a","needs":["d"],"deps":["b"]},{"name":"b","needs":["d"],"deps":["a"]}],"needs":[{"domain":"d","scope":"x"}],"produces":[]}}}' | ConvertFrom-Json
if (@(Get-GatherSpecIssues -Spec $probeCycle).Count -eq 0) {
    $violations.Add('[S7] 负对照失效：成环样本未被捕获')
}

$probeStep = '{"schema":"gather-spec-v1","step":1,"views":{"v":{"stages":[],"needs":[],"produces":[]}}}' | ConvertFrom-Json
if (@(Get-ForbiddenKeyHits -Node $probeStep -Path '$').Count -eq 0) {
    $violations.Add('[S7] 负对照失效：step 键样本未被捕获')
}

# S8 负对照（回归样本）：声明与引用「同名同错」（camelCase 且两侧一致）必须被判红 —— 这正是 R9/R10 纯字符串对账漏掉的那类。
$probeName = '{"schema":"gather-spec-v1","views":{"v":{"stages":[{"name":"capture","needs":["rawRound"],"deps":[]}],"needs":[{"domain":"rawRound","scope":"round"}],"produces":[]}}}' | ConvertFrom-Json
if (@(Get-GatherSpecIssues -Spec $probeName).Count -eq 0) {
    $violations.Add('[S8] 负对照失效：camelCase 命名样本未被捕获')
}

# S9/S10 负对照（组合缺陷必须被判红）。
$probeBase = '{"schema":"gather-spec-v1","views":{"base":{"stages":[{"name":"s","needs":["d"],"deps":[]}],"needs":[{"domain":"d","scope":"x"}],"produces":[{"name":"p","kind":"k","retention":"c","crit":"micro"}]},"child":{"extends":"base"}}}' | ConvertFrom-Json
$okProbe = [System.Collections.Generic.List[string]]::new()
$null = Get-EffectiveSpec -Spec $probeBase -Issues $okProbe
if (@($okProbe).Count -ne 0) { $violations.Add("[S9] 正对照失效：合法 extends 被误判（$($okProbe -join '；')）") }
$probeEffective = Get-EffectiveSpec -Spec $probeBase -Issues ([System.Collections.Generic.List[string]]::new())
if (@($probeEffective.views.child.stages).Count -eq 0) { $violations.Add('[S11] 组合折叠失效：child 未继承 base 阶段') }

$probeGhostBase = '{"schema":"gather-spec-v1","views":{"child":{"extends":"ghost"}}}' | ConvertFrom-Json
$ghostIssues = [System.Collections.Generic.List[string]]::new()
$null = Get-EffectiveSpec -Spec $probeGhostBase -Issues $ghostIssues
if (@($ghostIssues).Count -eq 0) { $violations.Add('[S9] 负对照失效：extends 未声明视图未被捕获') }

$probeGhostExclude = '{"schema":"gather-spec-v1","views":{"base":{"stages":[{"name":"s","needs":["d"],"deps":[]}],"needs":[{"domain":"d","scope":"x"}],"produces":[]},"child":{"extends":"base","excludeProduces":["nope"]}}}' | ConvertFrom-Json
$excludeIssues = [System.Collections.Generic.List[string]]::new()
$null = Get-EffectiveSpec -Spec $probeGhostExclude -Issues $excludeIssues
if (@($excludeIssues).Count -eq 0) { $violations.Add('[S10] 负对照失效：排除不存在的产物未被捕获') }

if ($ShowDetail) {
    Write-Host "  声明文件：$($files.Count) 个 / 视图：$totalViews 个" -ForegroundColor Gray
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 聚拢声明守卫：0 违规（合格性/闭合/无步骤词/正负对照在位）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 聚拢声明守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
