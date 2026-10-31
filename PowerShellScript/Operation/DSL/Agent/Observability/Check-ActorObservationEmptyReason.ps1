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
      E6 基础提示词分段在位：HistoryBoardModel 含 `BasePromptSegment` ∧ 视图服务按 `prompt:l0` 分段 ∧ 渲染器用统一渲染项。
      E7 能力目录退役：渲染器/视图投影不得再出现 `actor-dir` / `ActorDirectory` / `ProjectDirectory`。
      E8 结果账唯一写点：SubAgentResultPublisher 含 ResultObservationLedger.Record（Node/Ktf/None 三出口）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

# 上下文/裁决（根定位 + 违规清单 + 统一出口）
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ActorObservationEmptyReason'
$Root = $ctx.Root
$violations = $ctx.Violations
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

function Test-NotContains([string]$Relative, [string]$Pattern, [string]$Code, [string]$Message) {
    $path = Join-Path $agent $Relative
    if (-not (Test-Path -LiteralPath $path)) {
        $script:violations.Add("[$Code] 缺件：$Relative")
        return
    }
    $text = [System.IO.File]::ReadAllText($path)
    if ($text -match $Pattern) { $script:violations.Add("[$Code] $Message") }
}

Test-Contains 'Agent.Ui\Ui\Views\Projection\ActorObservationViewProjector.cs' 'EmptyText' 'E1' '缺 EmptyText（空声明显式占位）'
Test-Contains 'Agent.Ui\Ui\Views\Projection\ActorObservationViewProjector.cs' 'NotAttachedText' 'E1' '缺 NotAttachedText（未装配显式占位）'
Test-Contains 'Agent.Session\Projection\ActorObservationRules.cs' '"declaration-source-not-attached"' 'E2' '缺声明源未装配归因（常量单源）'
Test-Contains 'Agent.Session\Projection\ActorObservationRules.cs' '"task-runtime-source-not-attached"' 'E2' '缺任务源未装配归因（常量单源）'
Test-Contains 'Agent.Session\Projection\ActorObservationRules.cs' '"history-scope-not-enumerated"' 'E2' '缺全局 scope 未枚举归因（常量单源）'
Test-Contains 'Agent.Session\Projection\ActorObservationRules.cs' '"no-invocation"' 'E2' '缺「无调用」归因（声明不产行）'
Test-Contains 'Agent.Session\Projection\ActorObservationHub.cs' 'ActorResultReasons\.' 'E2' 'Hub 未消费归因常量（裸字面量回归）'
Test-Contains 'Agent.Session\Projection\ActorObservationRules.cs' '"unregistered-invocation"' 'E3' '缺未登记调用结构码'
Test-Contains 'Agent.Session\Projection\ActorObservationProjector.cs' 'ActorResultReasons\.UnregisteredInvocation' 'E3' '投影器未消费未登记结构码'
Test-Contains 'Agent.Ui\Ui\Views\Projection\HistoryBoardModel.cs' 'ActorsEmpty' 'E4' '看板模型缺执行体空态字段'
Test-Contains 'Agent.Ui\Ui\Views\Rendering\ConversationPageRenderer.cs' 'RenderActorObservationPanel' 'E4' '渲染器缺执行体面板（空态无处承载）'
Test-Contains 'Agent.Session\Projection\ActorObservationRules.cs' '"result-not-published"' 'E5' '结果事实缺显式不可达原因常量（默认 None 静默）'
Test-Contains 'Agent.Ui\Core\Host\ActorFactObservationSource.cs' 'ActorResultReasons\.ResultNotPublished' 'E5' '结果事实未消费不可达原因常量'
Test-Contains 'Agent.Session\Projection\ResultObservationLedger.cs' 'public static bool Record' 'E9' '结果账 Record 必须返回 bool（拒绝而非抛）'
Test-NotContains 'Agent.Session\Projection\ResultObservationLedger.cs' 'throw new InvalidOperationException' 'E9' '结果账不得抛异常（观测不阻断业务）'
Test-Contains 'Agent.Ui\Core\Host\ActorFactObservationSource.cs' 'ActorResultReasons\.ResultReasonMissing' 'E9' '事实源缺 None+空原因兜底'
Test-Contains 'Agent.Ui\Ui\Views\Projection\HistoryBoardModel.cs' 'BasePromptSegment' 'E6' '看板模型缺基础提示词分段（L0 分文件）'
Test-Contains 'Agent.Ui\Ui\Views\Projection\PromptSurfaceResolver.cs' 'prompt:l0' 'E6' '基础提示词未按 prompt:l0 分段投影（Task 同模式）'
Test-Contains 'Agent.Ui\Ui\Views\Rendering\ConversationPageRenderer.cs' 'ConversationRenderItem' 'E6' '基础提示词未以统一渲染项承载（折叠模式不一致）'
Test-NotContains 'Agent.Ui\Ui\Views\Rendering\ConversationPageRenderer.cs' 'actor-dir|ActorDirectory' 'E7' '能力目录仍在渲染（历史对话不应展示能力目录）'
Test-NotContains 'Agent.Ui\Ui\Views\Projection\ActorObservationViewProjector.cs' 'ActorObservationDirectoryRow|ProjectDirectory' 'E7' '视图投影仍产能力目录（应退役）'
Test-Contains 'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs' 'ResultObservationLedger\.Record' 'E8' '结果账缺唯一写点（结果通道不可读）'
Test-Contains 'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs' 'ActorResultChannel\.Ktf' 'E8' '结果账缺 KTF 通道事实'
Test-Contains 'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs' 'ActorResultChannel\.Node' 'E8' '结果账缺节点通道事实'

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ActorObservationEmptyReason'
