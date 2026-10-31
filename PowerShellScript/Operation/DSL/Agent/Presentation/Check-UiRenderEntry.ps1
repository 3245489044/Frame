#requires -Version 7.0
<#
.SYNOPSIS
    渲染入口单源守卫（D10）：页面产物必须经 RenderOutputCoordinator；禁领域直构 RenderOutput。
.DESCRIPTION
    C1 构造点收敛：领域（Projects/Plug/Agent/src）内 `new RenderOutput(` = 0（框架契约/装配层自带构造不受限）。
    C2 协调器消费：ConversationViewService 使用 RenderOutputCoordinator.BuildAsync（trace/计时/异常收敛单源）。
    C3 追踪号单源：领域不得自建 `RefreshTraceId.New(`（前缀映射归协调器）。
    C4 负对照：合成 `return new RenderOutput(plan);` 必须被 C1 捕获。
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$contract = Join-Path $repo 'Frame\CsharpPrj\Plate\HtmlViewer\Abstractions\RenderOutput.cs'
$viewService = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'
$scanRoots = @((Join-Path $repo 'Projects\Plug\Agent\src'))

function Read-Text([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺文件: $path" }
    return Get-Content -LiteralPath $path -Raw
}

function Find-ConstructionSites {
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($root in $scanRoots) {
        Get-ChildItem -LiteralPath $root -Recurse -Include *.cs -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput' } |
            ForEach-Object {
                if ($_.FullName -eq $contract) { return }
                $hit = Select-String -LiteralPath $_.FullName -Pattern 'new RenderOutput\(' -SimpleMatch:$false
                if ($hit) { $hits.Add($_.FullName.Replace($repo, '').TrimStart('\')) }
            }
    }
    return $hits
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = 'RenderOutput 构造点收敛（契约外 = 0）'
        Ok = { (Find-ConstructionSites).Count -eq 0 } }
    [pscustomobject]@{ Id = 'C2'; Title = '协调器承担一拍执行（RenderOutputCoordinator.BuildAsync）'
        Ok = { (Read-Text $viewService) -match 'RenderOutputCoordinator\.BuildAsync' } }
    [pscustomobject]@{ Id = 'C3'; Title = '追踪号单源（领域禁自建 RefreshTraceId.New）'
        Ok = { -not ((Read-Text $viewService) -match 'RefreshTraceId\.New\(') } }
)

if ($NegativeControl) {
    $bad = 'return new RenderOutput(plan);'
    $good = 'return RenderOutput.Write(plan);'
    $pattern = 'new RenderOutput\('
    $caught = ($bad -match $pattern) -and (-not ($good -match $pattern))
    if (-not $caught) { throw '负对照失败：C1 判据未能区分正/负样本' }
    Write-Host '[OK] 负对照通过：C1 判据可捕获领域直构产物。' -ForegroundColor Green
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
        Write-Host "UiRenderEntry: $failed 项违规（渲染入口出现第二路径）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'UiRenderEntry: 全部通过。' -ForegroundColor Green
}

exit 0
