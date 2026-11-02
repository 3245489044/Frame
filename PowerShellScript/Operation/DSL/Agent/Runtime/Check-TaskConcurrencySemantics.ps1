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

# C1：组提交 API 实现点唯一（规范实现 = Share TaskPool；Agent 适配器仅委托，不计入）。
$defs = @($files | Where-Object {
    $_.Name -ne 'AgentTaskPool.cs' -and
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'public\s+async\s+Task<GroupSubmitReceipt>\s+SubmitGroupAsync\s*\('
})
if ($defs.Count -ne 1) { Add-Violation 'C1' "SubmitGroupAsync 实现点应为 1（Share TaskPool），实际 $($defs.Count)" }

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
        (Get-Content -LiteralPath $_.FullName -Raw) -match (':\s*' + [regex]::Escape($pair.Port) + '\b')
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

# C8：单一组合根——全仓 `new ConcurrencyBudgetGate(` 恰 1 处（组合根），禁第二门禁/双判定。
$gateNews = @($files | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match 'new\s+ConcurrencyBudgetGate\s*\(' })
if ($gateNews.Count -ne 1) {
    Add-Violation 'C8' "new ConcurrencyBudgetGate( 应恰 1 处（ConcurrencyComposition），实际 $($gateNews.Count)：$((@($gateNews | ForEach-Object Name)) -join ',')"
}

# C9：SplitPlan 未落节点不得拆分（受控码存在）。
$pipelineFile = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Engine\Concurrency\ConcurrencyDecisionPipeline.cs'
$pipelineText = if (Test-Path -LiteralPath $pipelineFile) { Get-Content -LiteralPath $pipelineFile -Raw } else { '' }
if ($pipelineText -notmatch 'CONCURRENCY_PLAN_NOT_PERSISTED') {
    Add-Violation 'C9' '缺少计划未落库拒绝（CONCURRENCY_PLAN_NOT_PERSISTED）'
}

# C10：组员唯一归属（提交期校验既有归属）。
$groupPolicyFile = Join-Path $repoRoot 'Frame\CsharpPrj\Share\Orchestration\TaskStatusPool\TaskPool\GroupPolicy.cs'
$groupPolicyText = if (Test-Path -LiteralPath $groupPolicyFile) { Get-Content -LiteralPath $groupPolicyFile -Raw } else { '' }
if (($poolText + $groupPolicyText) -notmatch 'GROUP_MEMBER_ALREADY_ENROLLED') {
    Add-Violation 'C10' '缺少组员唯一归属校验（GROUP_MEMBER_ALREADY_ENROLLED）'
}

# C11：容量单源（池实现 IGroupCapacitySource；门禁读同一来源）。
$gateFile = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Engine\Concurrency\ConcurrencyBudgetGate.cs'
$gateText = if (Test-Path -LiteralPath $gateFile) { Get-Content -LiteralPath $gateFile -Raw } else { '' }
if ($poolText -notmatch 'IGroupCapacitySource' -or $gateText -notmatch 'IGroupCapacitySource') {
    Add-Violation 'C11' '容量非单源（池/门禁未共用 IGroupCapacitySource）'
}

# C12：组节点复用统计有写者（由租约审计汇总，非恒零占位）。
if ($poolText -notmatch '_groupAudit\?\.Snapshot') {
    Add-Violation 'C12' 'TaskGroupResult.NodeReuse 无审计写者（疑似恒零占位）'
}

# C7：负对照（判据必须能判红）。
if ($NegativeControl) {
    $negative = @'
public Task<GroupSubmitReceipt> SubmitGroupAsync(TaskGroupDescriptor g, IReadOnlyList<T> items)
public Task<GroupSubmitReceipt> SubmitGroupAsync(TaskGroupDescriptor g, IReadOnlyList<T> items)
var x = GroupAggregate(1);
public sealed class Ghost : IConcurrencyBudgetGate { }
await Task.WhenAll(a, b);
var g1 = new ConcurrencyBudgetGate();
var g2 = new ConcurrencyBudgetGate();
'@
    $submitCountNeg = @([regex]::Matches($negative, 'public\s+async\s+Task<GroupSubmitReceipt>\s+SubmitGroupAsync\s*\(')).Count
    $negativeChecks = @{
        C1 = ($submitCountNeg -ne 1)
        C2 = ($negative -match '(GroupAggregate|AggregateGroups|CrossGroupAggregate)')
        C5 = ($negative -match ':\s*IConcurrencyBudgetGate\b')
        C6 = ($negative -match 'Task\.WhenAll')
        C8 = (@([regex]::Matches($negative, 'new\s+ConcurrencyBudgetGate\s*\(')).Count -ne 1)
    }
    if (-not ($negativeChecks.C1 -and $negativeChecks.C2 -and $negativeChecks.C5 -and $negativeChecks.C6 -and $negativeChecks.C8)) {
        Add-Violation 'C7' '负对照未触发（判据恒真/过窄）'
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] TaskConcurrencySemantics：C1–C12 通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] TaskConcurrencySemantics 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
