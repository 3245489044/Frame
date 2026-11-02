# Check-TaskConcurrencySemantics.ps1
# 任务池并发语义守卫（C1–C7）：组 API 唯一实现 / 无跨组聚合 / 嵌套校验 / 容量门次序 /
# L0 三端口唯一 / 领域零内联并发。判据载体 = 源码结构事实（禁执行者叙述）。
# 用法：pwsh -File Check-TaskConcurrencySemantics.ps1 [-Check] [-NegativeControl]
# 退出码：0 通过（或未 -Check 的报告模式）；1 违规；2 依赖缺失。

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

function Get-CsFiles([string[]]$roots) {
    foreach ($root in $roots) {
        if (Test-Path -LiteralPath $root) {
            Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cs |
                Where-Object { $_.FullName -notmatch '\\\\(obj|bin)\\\\' }
        }
    }
}

$roots = @(
    (Join-Path $repoRoot 'Frame\CsharpPrj'),
    (Join-Path $repoRoot 'Projects\Plug\Agent\src')
)
$files = @(Get-CsFiles $roots)

# C1：组提交 API 定义点唯一（Agent 侧只允许委托调用）。
$defs = @($files | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'Task<GroupSubmitReceipt>\s+SubmitGroupAsync\s*\('
})
if ($defs.Count -ne 1) { Add-Violation 'C1' "SubmitGroupAsync 定义点应为 1，实际 $($defs.Count)" }

# C2：无跨组聚合 API。
foreach ($f in $files) {
    if ((Get-Content -LiteralPath $f.FullName -Raw) -match '(GroupAggregate|AggregateGroups|CrossGroupAggregate)') {
        Add-Violation 'C2' "出现跨组聚合符号：$($f.FullName)"
    }
}

# C3：嵌套组校验存在。
$poolFile = Join-Path $repoRoot 'Frame\CsharpPrj\Share\Orchestration\TaskStatusPool\TaskPool\TaskPool.cs'
$poolText = if (Test-Path -LiteralPath $poolFile) { Get-Content -LiteralPath $poolFile -Raw } else { '' }
if ($poolText -notmatch 'GROUP_NESTED' -or $poolText -notmatch 'TaskGroupScope') {
    Add-Violation 'C3' '组嵌套校验缺失（GROUP_NESTED / TaskGroupScope）'
}

# C4：容量门先于取件/置 Running。
$acquireAt = $poolText.IndexOf('AcquireCapacityAsync(ct)', [StringComparison]::Ordinal)
$runningAt = $poolText.IndexOf('UpdateStatusAsync(key, TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running', [StringComparison]::Ordinal)
if ($acquireAt -lt 0 -or $runningAt -lt 0 -or $acquireAt -gt $runningAt) {
    Add-Violation 'C4' "容量门次序错误（acquire=$acquireAt, running=$runningAt）"
}

# C5：L0 三端口实现唯一。
foreach ($pair in @(
    @{ Port = 'IComplexityAssessor'; Expect = 1 },
    @{ Port = 'ISplitPlanner'; Expect = 1 },
    @{ Port = 'IConcurrencyBudgetGate'; Expect = 1 })) {
    $count = @($files | Where-Object {
        (Get-Content -LiteralPath $_.FullName -Raw) -match (':' + [regex]::Escape($pair.Port) + '\b')
    }).Count
    if ($count -ne $pair.Expect) { Add-Violation 'C5' "$($pair.Port) 实现数应为 $($pair.Expect)，实际 $count" }
}

# C6：领域（Response）零内联并发。
$responseRoot = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Modules\Response'
foreach ($f in @(Get-CsFiles @($responseRoot))) {
    if ((Get-Content -LiteralPath $f.FullName -Raw) -match 'Task\.WhenAll') {
        Add-Violation 'C6' "领域内联并发：$($f.FullName)"
    }
}

# C7：负对照（判据必须能判红）。
if ($NegativeControl) {
    $negative = @'
public Task<GroupSubmitReceipt> SubmitGroupAsync(TaskGroupDescriptor g, IReadOnlyList<T> items)
public Task<GroupSubmitReceipt> SubmitGroupAsync(TaskGroupDescriptor g, IReadOnlyList<T> items)
var x = GroupAggregate(1);
public sealed class Ghost : IConcurrencyBudgetGate { }
await Task.WhenAll(a, b);
'@
    $submitCountNeg = @([regex]::Matches($negative, 'Task<GroupSubmitReceipt>\s+SubmitGroupAsync\s*\(')).Count
    $negativeChecks = @{
        C1 = ($submitCountNeg -ne 1)
        C2 = ($negative -match '(GroupAggregate|AggregateGroups|CrossGroupAggregate)')
        C5 = ($negative -match ':IConcurrencyBudgetGate\b')
        C6 = ($negative -match 'Task\.WhenAll')
    }
    if (-not ($negativeChecks.C1 -and $negativeChecks.C2 -and $negativeChecks.C5 -and $negativeChecks.C6)) {
        Add-Violation 'C7' '负对照未触发（判据恒真/过窄）'
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] TaskConcurrencySemantics：C1–C7 通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] TaskConcurrencySemantics 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
