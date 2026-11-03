# Check-ConcurrencyLayering.ps1
# 并发/任务框架分层守卫（B5）：L1 通用内核零 Frame 依赖；L4 任务编排零 Agent 依赖；
# Agent 侧不得复活已迁移类型（同一类型全仓只有一个实现点）。
# 判据载体 = 源码结构与 csproj 引用（禁执行者叙述）。退出码：0 通过 / 1 违规 / 2 依赖缺失。

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'Projects\Plug\Agent'))) {
    $parent = Split-Path $repoRoot -Parent
    if ($parent -eq $repoRoot) { $repoRoot = $null; break }
    $repoRoot = $parent
}
if (-not $repoRoot) { Write-Host '[FAIL] 无法定位仓库根'; exit 2 }

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$code, [string]$message) { $violations.Add("[$code] $message") | Out-Null }

function Get-CsFiles([string]$root) {
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
}

$concurrencyRoot = Join-Path $repoRoot 'Frame\CsharpPrj\Modules\Concurrency'
$tasksRoot = Join-Path $repoRoot 'Frame\CsharpPrj\Operation\HierarchyMethod\Tasks'
$agentSrc = Join-Path $repoRoot 'Projects\Plug\Agent\src'

# L1-1：Modules.Concurrency 零 Frame 命名空间引用。
foreach ($f in @(Get-CsFiles $concurrencyRoot)) {
    if ((Get-Content -LiteralPath $f.FullName -Raw) -match '(?m)^\s*using\s+(Share|Operation|Agent)\.') {
        Add-Violation 'L1-1' "通用并发内核引用了 Frame 命名空间：$($f.Name)"
    }
}

# L1-2：Modules.Concurrency 零 ProjectReference（BCL-only）。
$concurrencyProj = Join-Path $concurrencyRoot 'Concurrency.csproj'
if (-not (Test-Path -LiteralPath $concurrencyProj)) {
    Add-Violation 'L1-2' '缺少 Modules\Concurrency\Concurrency.csproj'
} elseif ((Get-Content -LiteralPath $concurrencyProj -Raw) -match '<ProjectReference') {
    Add-Violation 'L1-2' 'Modules.Concurrency 出现 ProjectReference（应为 BCL-only）'
}

# L4-1：Operation.HierarchyMethod.Tasks 零 Agent 命名空间引用。
foreach ($f in @(Get-CsFiles $tasksRoot)) {
    if ((Get-Content -LiteralPath $f.FullName -Raw) -match '(?m)^\s*using\s+Agent\.') {
        Add-Violation 'L4-1' "任务编排层引用了 Agent 命名空间：$($f.Name)"
    }
}

# F-1：Agent 侧不得复活已迁移类型（重复实现点）。
$migrated = @(
    'ConcurrencyBudgetGate.cs',
    'ConcurrencyComposition.cs',
    'ConcurrencyCostRecorder.cs',
    'ConcurrencyDecisionPipeline.cs',
    'DeclarativeComplexity.cs',
    'GroupKeyPolicy.cs',
    'TaskOrchestrator.cs',
    'TaskCommandHandler.cs',
    'TaskGraphService.cs',
    'AgentTaskQuery.cs',
    'TaskQueryUnavailableException.cs'
)
if (Test-Path -LiteralPath $agentSrc) {
    foreach ($name in $migrated) {
        $hits = @(Get-ChildItem -LiteralPath $agentSrc -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
        if ($hits.Count -gt 0) {
            Add-Violation 'F-1' "已迁移类型在 Agent 侧复活：$name"
        }
    }
}

# N-1：负对照（判据必须能判红）。
if ($NegativeControl) {
    $negative = @'
using Share.TaskStatusPool.TaskPool;
<ProjectReference Include="$(Proj_TaskStatusPool)" />
'@
    $negativeChecks = @(
        ($negative -match '(?m)^\s*using\s+(Share|Operation|Agent)\.'),
        ($negative -match '<ProjectReference')
    )
    if (-not ($negativeChecks[0] -and $negativeChecks[1])) {
        Add-Violation 'N-1' '负对照未触发（判据恒真/过窄）'
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] ConcurrencyLayering：L1/L4/F 分层与迁移唯一性通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] ConcurrencyLayering 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
