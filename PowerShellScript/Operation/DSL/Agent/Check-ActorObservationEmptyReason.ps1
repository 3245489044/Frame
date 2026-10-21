#requires -Version 5.1
<#
.SYNOPSIS
    执行体观测空态/结果通道可见守卫（ActorObservationEmptyReason）—— AX.NoSilentOutlet。

.DESCRIPTION
    判据：
      E1 呈现侧三态文案在位：ActorObservationViewProjector 含 EmptyText ∧ NotAttachedText。
      E2 Hub 归因键在位：declaration-source-not-attached / task-runtime-source-not-attached / history-scope-not-enumerated / no-invocation。
      E3 未登记调用显式标记：ActorObservationProjector 含 `unregistered-invocation`。
      E4 面板空态承载：HistoryBoardModel 含 `ActorsEmpty` ∧ ConversationPageRenderer 含 `RenderActorObservationPanel`。
      E5 结果通道缺失显式：ActorFactObservationSource 含 `result-not-published`（不默认静默 None）。
      E6 目录区独立成段：视图投影含 DirectoryEmptyText，渲染器含 `actor-dir`。
      E7 结果账唯一写点：SubAgentResultPublisher 含 ResultObservationLedger.Record（Node/Ktf/None 三出口）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$agent = Join-Path $Root 'Projects\Plug\Agent\src'

Write-Host ''
Write-Host '============================================================'
Write-Host '  执行体观测空态可见守卫（ActorObservationEmptyReason）'
Write-Host '============================================================'

function Test-Contains([string]$Relative, [string]$Pattern, [string]$Code, [string]$Message) {
    $path = Join-Path $agent $Relative
    if (-not (Test-Path -LiteralPath $path)) {
        $script:violations.Add("[$Code] 缺件：$Relative")
        return
    }
    $text = [System.IO.File]::ReadAllText($path)
    if ($text -notmatch $Pattern) { $script:violations.Add("[$Code] $Message") }
}

Test-Contains 'Agent.Ui\Ui\Views\ActorObservationViewProjector.cs' 'EmptyText' 'E1' '缺 EmptyText（空声明显式占位）'
Test-Contains 'Agent.Ui\Ui\Views\ActorObservationViewProjector.cs' 'NotAttachedText' 'E1' '缺 NotAttachedText（未装配显式占位）'
Test-Contains 'Agent.Session\Projection\ActorObservationHub.cs' 'declaration-source-not-attached' 'E2' '缺声明源未装配归因'
Test-Contains 'Agent.Session\Projection\ActorObservationHub.cs' 'task-runtime-source-not-attached' 'E2' '缺任务源未装配归因'
Test-Contains 'Agent.Session\Projection\ActorObservationHub.cs' 'history-scope-not-enumerated' 'E2' '缺全局 scope 未枚举归因'
Test-Contains 'Agent.Session\Projection\ActorObservationHub.cs' 'no-invocation' 'E2' '缺「无调用」归因（声明不产行）'
Test-Contains 'Agent.Session\Projection\ActorObservationProjector.cs' 'unregistered-invocation' 'E3' '缺未登记调用可见标记'
Test-Contains 'Agent.Ui\Ui\Views\HistoryBoardModel.cs' 'ActorsEmpty' 'E4' '看板模型缺执行体空态字段'
Test-Contains 'Agent.Ui\Ui\Views\ConversationPageRenderer.cs' 'RenderActorObservationPanel' 'E4' '渲染器缺执行体面板（空态无处承载）'
Test-Contains 'Agent.Ui\Core\Host\ActorFactObservationSource.cs' 'result-not-published' 'E5' '结果事实缺显式不可达原因（默认 None 静默）'
Test-Contains 'Agent.Ui\Ui\Views\ActorObservationViewProjector.cs' 'DirectoryEmptyText' 'E6' '目录区缺空态文案'
Test-Contains 'Agent.Ui\Ui\Views\ConversationPageRenderer.cs' 'actor-dir' 'E6' '渲染器缺目录区（声明应独立成段）'
Test-Contains 'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs' 'ResultObservationLedger\.Record' 'E7' '结果账缺唯一写点（结果通道不可读）'
Test-Contains 'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs' 'ActorResultChannel\.Ktf' 'E7' '结果账缺 KTF 通道事实'
Test-Contains 'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs' 'ActorResultChannel\.Node' 'E7' '结果账缺节点通道事实'

if ($violations.Count -eq 0) {
    Write-Host '  [成功] E1–E7 通过（空/未知/未装配/结果通道均显式可见）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
