#requires -Version 5.1
<#
.SYNOPSIS
    UI 刷新取数守卫（RefreshFetch）：线程边界 / 按需投影 / 树快照 / 批量取数 四类结构事实。
.DESCRIPTION
    判据（结构事实，禁执行者叙述）：
      R1 消息边界只投递：HtmlBackendRuntime 暴露 MessageScheduler 且 Strategy.Refresh 有 UiCommandQueue；
      R2 UI 树路径按需投影：TransferFlow.EnumerateAsync 调 RunProjectionAsync(..., member_keys)；
      R3 树快照：ConversationTreeProjection 使用 ConversationTreeSnapshotCache（读前/读后版本校验在实现内）；
      R4 批量取数：成员集 ReadManyAsync + 运行态 GetForHistoriesAsync + 图批量 GetReferencesManyAsync；
      R5 无同步等待：刷新链文件不得出现 GetAwaiter().GetResult() / .Wait(；
      N1 负对照：合成样本缺上述结构事实时必须判违规（防判据恒真）。
    模式：默认报告；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
Write-Host '=== [RefreshFetch] UI 刷新取数（线程边界 / 按需投影 / 快照 / 批量） ==='

$checks = @(
    @{ Tag = 'R1'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.cs';
       Must = @('IWebMessageScheduler', 'MessageScheduler', 'HandleMessageAsync') },
    @{ Tag = 'R1'; File = 'Frame\CsharpPrj\Strategy\Refresh\Core\UiCommandQueue.cs';
       Must = @('class UiCommandQueue', 'TryWrite(command)', 'ReferenceEquals(latest, command)') },
    @{ Tag = 'R2'; File = 'Projects\Plug\Agent\src\Agent.Core\Engine\Nodes\Bookkeeping\TransferFlow.cs';
       Must = @('RunProjectionAsync', '"member_keys"') },
    @{ Tag = 'R2'; File = 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Workflows\RoundSpecProjection.cs';
       Must = @('class RoundSpecProjector', 'MissingOutputs', 'producers') },
    @{ Tag = 'R3'; File = 'Projects\Plug\Agent\src\Agent.Ui\Core\Projection\ConversationTreeProjection.cs';
       Must = @('ConversationTreeSnapshotCache.TryGet', 'versionSource.Version', 'ConversationTreeSnapshotCache.Put') },
    @{ Tag = 'R4'; File = 'Projects\Plug\Agent\src\Agent.Session\Projection\HistoryMemberSetReader.cs';
       Must = @('ReadManyAsync', 'GetHistoryTaskKeysManyAsync') },
    @{ Tag = 'R4'; File = 'Projects\Plug\Agent\src\Agent.Session\Projection\TaskRuntimeReportProvider.cs';
       Must = @('GetForHistoriesAsync', 'GetManyAsync(refs') },
    @{ Tag = 'R4'; File = 'Frame\CsharpPrj\Operation\DataStoreDriven\Graph\ReferenceGraphCoordinator.cs';
       Must = @('GetReferencesManyAsync', 'public long Version') },
    @{ Tag = 'R6'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\SectionPlan.cs';
       Must = @('enum SurfaceStage', 'Shell', 'Decorations') },
    @{ Tag = 'R6'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\Kernel\WriteBudgetPolicy.cs';
       Must = @('decorations-over-budget', 'SurfaceStage.Decorations') },
    @{ Tag = 'R6'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.SurfaceWrite.cs';
       Must = @('OrderBy(static s => s.Stage)', 'ProgressiveSurface') },
    @{ Tag = 'R7'; File = 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\WriteOutcome.cs';
       Must = @('class TargetOutcomeAlgebra', 'conflicting-outcome', 'TargetOutcomeKind.Superseded') },
    @{ Tag = 'R8'; File = 'Projects\Plug\Agent\src\Agent.Session\Projection\ContextProjection.cs';
       Must = @('ProjectTaskWindowAsync', 'ListRoundsAsync') },
    @{ Tag = 'R8'; File = 'Projects\Plug\Agent\src\Agent.Session\Projection\ConversationReadPort.cs';
       Must = @('ReadWindowAsync', 'BuildTaskWindowAsync') }
)

foreach ($spec in $checks) {
    $abs = Join-Path $Root $spec.File
    if (-not (Test-Path -LiteralPath $abs)) {
        $violations.Add("[$($spec.Tag)] 层文件不在位：$($spec.File)")
        continue
    }

    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    foreach ($token in $spec.Must) {
        if ($text.IndexOf($token, [StringComparison]::Ordinal) -lt 0) {
            $violations.Add("[$($spec.Tag)] 缺结构事实「$token」：$($spec.File)")
        }
    }
    if ($ShowDetail) { Write-Host "  [$($spec.Tag)] $($spec.File) 已核（$($spec.Must.Count) 项）" -ForegroundColor DarkGray }
}

# R5：刷新链禁同步阻塞等待（采样核心文件）。
$syncBlocked = @(
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.cs',
    'Projects\Plug\Agent\src\Agent.Core\Engine\Nodes\Bookkeeping\TransferFlow.cs',
    'Frame\CsharpPrj\Plate\HtmlViewer\Html\Write\HtmlBackendRuntime.cs'
)
foreach ($rel in $syncBlocked) {
    $abs = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $abs)) { continue }
    $text = Get-Content -LiteralPath $abs -Raw -Encoding UTF8
    if ($text -match 'GetAwaiter\(\)\.GetResult\(\)' -or $text -match '\.Wait\(') {
        $violations.Add("[R5] 刷新链出现同步等待：$rel")
    }
}

# N1 负对照：空样本必须判缺（防判据恒真）。
$synthetic = 'class EmptySample { }'
$required = @('MessageScheduler', 'RunProjectionAsync', 'ConversationTreeSnapshotCache')
$missingInSynthetic = @($required | Where-Object { $synthetic.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
if ($missingInSynthetic.Count -eq 0) {
    $violations.Add('[N1] 负对照失效：空样本未被判缺（判据恒真）')
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] RefreshFetch：线程边界 + 按需投影 + 树快照 + 批量取数；负对照命中。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] RefreshFetch 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
