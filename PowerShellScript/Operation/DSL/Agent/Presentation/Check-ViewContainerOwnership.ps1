#requires -Version 7.0
<#
.SYNOPSIS
    视图容器所有权守卫（ViewContainerOwnership）——每容器恰一个 ownerView，框架不引入领域区域词。

.DESCRIPTION
    容器派生单源 = Agent.Ui 生成产物 ui-declaration.generated.json（C# Provider 与门禁读同一份）。
    C1 容器非空：每视图至少一个容器（视图不可挂载 ⇒ 红）
    C2 所有者一致：containers[].owner == viewKey（禁跨视图共享同一 DOM 容器）
    C3 容器键唯一：同一视图内容器键不重复
    C4 流容器至多一个（append-only 面单实例）
    C5 框架零领域词：Frame\Plate\HtmlViewer 与 Operation\FrontEndDriven\HtmlViewerBridge 源码不得出现 UiRegionKind
    C6 单源：禁 view-programs.generated.json 回流
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
$declaration = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\ui-declaration.generated.json'
$retiredGenerated = Join-Path $repo 'Projects\Plug\Agent\Config\ui\view-programs.generated.json'
$frameRoots = @(
    (Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer'),
    (Join-Path $repo 'Frame\CsharpPrj\Operation\FrontEndDriven\HtmlViewerBridge')
)

function Get-Views {
    # 派生单源 = 产物（禁本地再派生壳槽/区域集）。
    if (-not (Test-Path -LiteralPath $declaration)) { throw "产物缺失：$declaration（先跑 Generate-UiDeclaration.ps1 -Apply）" }
    $decl = Get-Content -LiteralPath $declaration -Raw -Encoding UTF8 | ConvertFrom-Json
    $views = [System.Collections.Generic.List[object]]::new()
    foreach ($v in @($decl.views)) {
        if ($null -eq $v) { continue }
        $viewKey = [string]$v.view
        $containers = @(@($decl.containers) |
            Where-Object { [string]$_.view -eq $viewKey } |
            ForEach-Object {
                [pscustomobject]@{ key = [string]$_.key; ownerView = [string]$_.owner; role = [string]$_.role }
            })
        $views.Add([pscustomobject]@{ viewKey = $viewKey; containers = $containers })
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
                    ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) } |
                    Select-String -Pattern '\bUiRegionKind\b' -AllMatches |
                    ForEach-Object { $_.Matches.Count } |
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
        try { $pass = [bool](& $r.Ok) } catch {
            Write-Host ("[FAIL] {0} {1} — {2}" -f $r.Id, $r.Title, $_.Exception.Message) -ForegroundColor Red
            $pass = $false
        }
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
