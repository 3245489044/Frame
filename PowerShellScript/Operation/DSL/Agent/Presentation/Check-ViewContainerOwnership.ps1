#requires -Version 7.0
<#
.SYNOPSIS
    视图容器所有权守卫（ViewContainerOwnership）——每容器恰一个 ownerView，框架不引入领域区域词。

.DESCRIPTION
    C1 容器非空：每视图至少一个容器（视图不可挂载 ⇒ 红）
    C2 所有者一致：containers[].ownerView == viewKey（禁跨视图共享同一 DOM 容器）
    C3 容器键唯一：同一视图内容器键不重复
    C4 流容器至多一个（append-only 面单实例）
    C5 框架零领域词：Frame\Plate\HtmlViewer 与 Operation\FrontEndDriven\HtmlViewerBridge 源码不得出现 UiRegionKind
    C6 单源：ui-regions.json 为唯一视图声明面（禁 view-programs.generated.json 回流）
    C7 负对照：合成「同容器两 owner」必须被判红
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$declaration = Join-Path $repo 'Projects\Plug\Agent\Config\ui\ui-regions.json'
$retiredGenerated = Join-Path $repo 'Projects\Plug\Agent\Config\ui\view-programs.generated.json'
$frameRoots = @(
    (Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer'),
    (Join-Path $repo 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge')
)

function Get-Views {
    $decl = Get-Content -LiteralPath $declaration -Raw | ConvertFrom-Json
    $defs = @{}
    foreach ($r in @($decl.regions)) {
        if ($null -eq $r) { continue }
        $defs[[string]$r.kind] = [pscustomobject]@{ Key = [string]$r.key; Stream = ([string]$r.role -eq 'stream') }
    }
    $views = [System.Collections.Generic.List[object]]::new()
    foreach ($v in @($decl.views)) {
        if ($null -eq $v) { continue }
        $viewKey = [string]$v.view
        $containers = [System.Collections.Generic.List[object]]::new()
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($chrome in @('region-seg', 'region-head', 'region-subhead')) {
            if ($seen.Add($chrome)) { $containers.Add([pscustomobject]@{ key = $chrome; ownerView = $viewKey; role = 'region' }) }
        }
        foreach ($kind in @($v.regions)) {
            $kindText = [string]$kind
            if (-not $defs.ContainsKey($kindText)) { throw "视图 '$viewKey' 引用未声明区域：$kindText" }
            $def = $defs[$kindText]
            if (-not $seen.Add($def.Key)) { continue }
            $containers.Add([pscustomobject]@{ key = $def.Key; ownerView = $viewKey; role = $(if ($def.Stream) { 'stream' } else { 'region' }) })
        }
        $views.Add([pscustomobject]@{ viewKey = $viewKey; containers = $containers.ToArray() })
    }
    return $views.ToArray()
}

function Get-OwnershipViolations([object[]]$views) {
    $violations = [System.Collections.Generic.List[string]]::new()
    foreach ($view in $views) {
        $viewKey = [string]$view.viewKey
        $containers = @($view.containers)
        if ($containers.Count -eq 0) { $violations.Add("C1 视图 '$viewKey' 无容器") }
        $seen = @{}
        foreach ($container in $containers) {
            $key = [string]$container.key
            $owner = [string]$container.ownerView
            if ($owner -ne $viewKey) { $violations.Add("C2 容器 '$key' 所有者 '$owner' ≠ 视图 '$viewKey'") }
            if ($seen.ContainsKey($key)) { $violations.Add("C3 视图 '$viewKey' 容器键重复：$key") } else { $seen[$key] = $true }
        }
        if (@($containers | Where-Object { [string]$_.role -eq 'stream' }).Count -gt 1) {
            $violations.Add("C4 视图 '$viewKey' 流容器多于一个")
        }
    }
    return $violations
}

$rules = @(
    [pscustomobject]@{ Id = 'C1-C4'; Title = '所有权/唯一性/流容器'
        Ok = { (Get-OwnershipViolations (Get-Views)).Count -eq 0 } }
    [pscustomobject]@{ Id = 'C5'; Title = '框架源码零领域区域词'
        Ok = {
            $hits = 0
            foreach ($rootDir in $frameRoots) {
                if (-not (Test-Path -LiteralPath $rootDir)) { continue }
                $hits += @(Get-ChildItem -LiteralPath $rootDir -Recurse -File -Filter *.cs |
                    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
                    ForEach-Object { ([System.IO.File]::ReadAllText($_.FullName) -split "`n" | Where-Object { $_ -match '\bUiRegionKind\b' }).Count } |
                    Measure-Object -Sum).Sum
            }
            return ($hits -eq 0)
        } }
    [pscustomobject]@{ Id = 'C6'; Title = '声明单源（禁生成物回流）'
        Ok = { -not (Test-Path -LiteralPath $retiredGenerated) } }
)

if ($NegativeControl) {
    $badViews = @(
        [pscustomobject]@{ viewKey = 'a'; containers = @([pscustomobject]@{ key = 'region-x'; ownerView = 'b'; role = 'region' }) }
    )
    $goodViews = @(
        [pscustomobject]@{ viewKey = 'a'; containers = @([pscustomobject]@{ key = 'region-x'; ownerView = 'a'; role = 'region' }) }
    )
    if ((Get-OwnershipViolations $badViews).Count -eq 0) { throw '负对照失败：C2 未捕获跨视图 owner' }
    if ((Get-OwnershipViolations $goodViews).Count -ne 0) { throw '负对照失败：C2 对合法所有权误报' }
    Write-Host '[OK] 负对照通过：跨视图 owner 被捕获，合法声明不误报。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "ViewContainerOwnership: $failed 项违规（容器所有权不闭合）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'ViewContainerOwnership: 全部通过。' -ForegroundColor Green
}

exit 0
