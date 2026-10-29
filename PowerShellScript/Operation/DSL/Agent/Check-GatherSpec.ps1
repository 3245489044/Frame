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
    模式：默认报告（不设退出码）；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$forbidden = @('step', 'order', 'sequence', 'then', 'next')

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

        $stages = @($view.stages)
        if ($stages.Count -eq 0) { $issues.Add("[S2] 视图 '$viewKey' 无 stages") }

        $needDomains = @($view.needs | ForEach-Object { [string]$_.domain })
        if ($needDomains.Count -eq 0) { $issues.Add("[S3] 视图 '$viewKey' 无 needs") }

        $stageNames = @($stages | ForEach-Object { [string]$_.name })
        $dupes = @($stageNames | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
        foreach ($dupe in $dupes) { $issues.Add("[S2] 视图 '$viewKey' stage 重名：'$dupe'") }

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

$declDir = Join-Path $Root 'Projects\Plug\Agent\Config\gather'
$files = @(Get-ChildItem -LiteralPath $declDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
if ($files.Count -eq 0) {
    $violations.Add("[S1] 聚拢声明缺失：$declDir\*.json（缺失即判红，禁回退 C# 常量）")
}

$totalViews = 0
foreach ($file in $files) {
    try { $spec = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json }
    catch { $violations.Add("[S1] $($file.Name) JSON 解析失败：$($_.Exception.Message)"); continue }

    foreach ($issue in (Get-GatherSpecIssues -Spec $spec)) { $violations.Add("$($file.Name) $issue") }
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
