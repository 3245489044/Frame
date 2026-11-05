#requires -Version 5.1
<#
.SYNOPSIS
    执行体观测面守卫（ActorObservationSurface）—— P5′-P7 调用实例驱动。

.DESCRIPTION
    判据：
      S1 观测合同/调用投影/合并/Hub/视图投影/调用台账在位。
      S2 UI 视图层零目录/任务池直读：`Agent.Ui\Ui\Views` 内 `ISubAgentCatalog` = 0 ∧ `AgentTaskPoolAdapter` = 0。
      S3 单一入口：UI 只经 `IActorObservationSource`（ConversationViewService 命中）。
      S4 全局 scope 能力在位：Hub 含 `ActorObservationScopes.Global`。
      S5 身份列只取 Id：视图投影含 `Id: string.IsNullOrWhiteSpace(row.Id)`（执行腿走独立字段）。
      S6 负对照：把 `ISubAgentCatalog` 注入样例必须被 S2 捕获（防判据空转）。
      S7 声明不产行：行必含 InvocationId，合并入参为 invocations（无声明先行建行）。
      S8 层级适用性被消费：Hub 含 levelApplicable ∧ 组合根含 ActorApplicability.For。
      S9 执行体面零控制：视图投影与渲染器不得出现 Pause/Resume/Step/Cancel 调用或命令字面量。
      S10 负对照：声明先行样例必须被 S7 捕获。

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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ActorObservationSurface'
$Root = $ctx.Root
$violations = $ctx.Violations
$agent = Join-Path $Root 'Projects\Plug\Agent\src'
$views = Join-Path $agent 'Agent.Ui\Ui\Views'
$required = @(
    'Agent.Session\Projection\ActorObservationContracts.cs',
    'Agent.Session\Projection\ActorObservationRules.cs',
    'Agent.Session\Projection\ActorInvocationProjector.cs',
    'Agent.Session\Projection\ActorInvocationLedger.cs',
    'Agent.Session\Projection\ResultObservationLedger.cs',
    'Agent.Session\Projection\ActorObservationProjector.cs',
    'Agent.Session\Projection\ActorObservationHub.cs',
    'Agent.Ui\Ui\Views\Projection\ActorObservationViewProjector.cs',
    'Agent.Ui\Ui\Views\Projection\PromptSnapshotResolver.cs',
    'Agent.Ui\Core\Host\ActorFactObservationSource.cs',
    'Agent.Modules\SubAgent\Runtime\SubAgentResultPublisher.cs'
)
Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Baselines\GateFileIndex.psm1') -Force

Write-Host ''
Write-Host '============================================================'
Write-Host '  执行体观测面守卫（ActorObservationSurface：P5′-P7）'
Write-Host '============================================================'

foreach ($rel in $required) {
    $path = Join-Path $agent $rel
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("[S1] 缺件：$rel") }
}

function Get-ViewViolations([string]$Text) {
    $hits = [System.Collections.Generic.List[string]]::new()
    if ($Text -match 'ISubAgentCatalog') { $hits.Add('视图层直读 ISubAgentCatalog（应经观测 Hub）') }
    if ($Text -match 'AgentTaskPoolAdapter') { $hits.Add('视图层直读 AgentTaskPoolAdapter（应经唯一供数口）') }
    return $hits
}

if (Test-Path -LiteralPath $views) {
    foreach ($file in (Get-GateFiles -Root $views -Extension cs -Exclude '\\(obj|bin|BuildOutput)\\')) {
        $text = [System.IO.File]::ReadAllText($file)
        foreach ($m in (Get-ViewViolations $text)) {
            $violations.Add("[S2] $([System.IO.Path]::GetFileName($file))：$m")
        }
    }
} else {
    $violations.Add("[S2] 缺件（判据不可判）：$views")
}

$probe = 'var catalog = AgentServiceSlots.Resolve<ISubAgentCatalog>();'
if ((Get-ViewViolations $probe).Count -eq 0) {
    $violations.Add('[S6] 负对照失效：注入目录的合成样本未被 S2 捕获')
}

$viewService = Join-Path $views 'Orchestration\ConversationViewService.cs'
if (Test-Path -LiteralPath $viewService) {
    $text = [System.IO.File]::ReadAllText($viewService)
    if ($text -notmatch 'IActorObservationSource') { $violations.Add('[S3] 视图服务未引用 IActorObservationSource（单一入口断链）') }
    if ($text -match 'new\s+TaskRuntimeReportProvider\s*\(') { $violations.Add('[S3] 视图服务自建 TaskRuntimeReportProvider（第二投影点）') }
} else {
    $violations.Add('[S3] 缺件（判据不可判）：ConversationViewService.cs')
}

$hub = Join-Path $agent 'Agent.Session\Projection\ActorObservationHub.cs'
if (Test-Path -LiteralPath $hub) {
    $text = [System.IO.File]::ReadAllText($hub)
    if ($text -notmatch 'ActorObservationScopes\.Global') { $violations.Add('[S4] Hub 缺全局 scope 分支') }
    if ($text -notmatch 'levelApplicable') { $violations.Add('[S8] Hub 未消费层级适用性谓词（容器行可能进入）') }
}

$assembly = Join-Path $agent 'Agent.Launcher\Composition\Hosting\Session\ConversationAssemblySteps.cs'
if (Test-Path -LiteralPath $assembly) {
    $text = [System.IO.File]::ReadAllText($assembly)
    if ($text -notmatch 'ActorApplicability\.For') { $violations.Add('[S8] 组合根未接 ActorApplicability.For（适用性单源断链）') }
} else {
    $violations.Add('[S8] 缺件（判据不可判）：ConversationAssemblySteps.cs')
}

$viewProjector = Join-Path $views 'Projection\ActorObservationViewProjector.cs'
if (Test-Path -LiteralPath $viewProjector) {
    $text = [System.IO.File]::ReadAllText($viewProjector)
    if ($text -notmatch 'Id: string\.IsNullOrWhiteSpace\(row\.Id\)') { $violations.Add('[S5] 身份列未以 Id 为主标签（执行腿可能冒充身份）') }
    if ($text -notmatch 'ExecutorId') { $violations.Add('[S5] 执行腿未作为独立实现细节字段承载') }
    if ($text -match 'PauseAsync|ResumeAsync|StepAsync|CancelAsync|data-cmd="(pause|resume|step|cancel)"') {
        $violations.Add('[S9] 执行体视图内出现控制调用/命令（执行体面零控制）')
    }
} else {
    $violations.Add('[S5] 缺件（判据不可判）：ActorObservationViewProjector.cs')
}

$renderer = Join-Path $views 'Rendering\ConversationPageRenderer.cs'
if (Test-Path -LiteralPath $renderer) {
    $text = [System.IO.File]::ReadAllText($renderer)
    if ($text -match 'PauseAsync|ResumeAsync|StepAsync|CancelAsync') {
        $violations.Add('[S9] 渲染器出现执行体控制调用（执行体面零控制）')
    }
} else {
    $violations.Add('[S9] 缺件（判据不可判）：ConversationPageRenderer.cs')
}

$contracts = Join-Path $agent 'Agent.Session\Projection\ActorObservationContracts.cs'
$projector = Join-Path $agent 'Agent.Session\Projection\ActorObservationProjector.cs'

function Get-DeclarationRowViolations([string]$Text) {
    $hits = [System.Collections.Generic.List[string]]::new()
    if ($Text -notmatch 'Merge\(\s*IReadOnlyList<ActorInvocation>') { $hits.Add('合并器未以调用实例为输入（声明先行未消除）') }
    if ($Text -match 'rows\[key\]\s*=\s*new\s+ActorObservationRow\s*\(') { $hits.Add('合并器仍按声明键直接建行（ANTI-DECLARATION-AS-ACTOR）') }
    # S7b：调用实例先行 —— 空调用早退必须发生在声明字典构建之前。
    $early = $Text.IndexOf('return Array.Empty<ActorObservationRow>')
    $decl = $Text.IndexOf('var declarationsById')
    if ($early -lt 0 -or $decl -lt 0 -or $early -gt $decl) { $hits.Add('合并顺序不成立：声明字典先于调用实例判断（声明先行）') }
    return $hits
}

if ((Test-Path -LiteralPath $contracts) -and (Test-Path -LiteralPath $projector)) {
    $contractText = [System.IO.File]::ReadAllText($contracts)
    if ($contractText -notmatch 'ActorInvocationKey') { $violations.Add('[S7] 合同缺 ActorInvocationKey（调用身份未建模）') }
    foreach ($m in (Get-DeclarationRowViolations ([System.IO.File]::ReadAllText($projector)))) {
        $violations.Add("[S7] $m")
    }
}

$probeDeclaration = 'foreach (var declaration in declarations) { rows[key] = new ActorObservationRow(Id, Kind, true, Stage, Origin); }'
if ((Get-DeclarationRowViolations $probeDeclaration).Count -eq 0) {
    $violations.Add('[S10] 负对照失效：声明先行合成样本未被 S7 捕获')
}

# S11 提示词快照读取单点（E-2/D-5）：视图服务不得直读，唯一读点 = PromptSnapshotResolver。
if (Test-Path -LiteralPath $viewService) {
    $text = [System.IO.File]::ReadAllText($viewService)
    if ($text -match 'IPromptSnapshotStore') {
        $violations.Add('[S11] 视图服务仍直读 IPromptSnapshotStore（须经 PromptSnapshotResolver 单点）')
    }
}
$resolver = Join-Path $views 'Projection\PromptSnapshotResolver.cs'
if (Test-Path -LiteralPath $resolver) {
    if ([System.IO.File]::ReadAllText($resolver) -notmatch '\.Read\(') {
        $violations.Add('[S11] PromptSnapshotResolver 未承载读点')
    }
} else {
    $violations.Add('[S11] 缺件（判据不可判）：PromptSnapshotResolver.cs')
}

# S12 阶段受控词对拍（D-1）：观测镜像表必须与框架唯一词表集合相等。
function Get-StageTokens([string]$Text) {
    $set = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($m in [regex]::Matches($Text, 'public const string \w+\s*=\s*"([a-z]+)"')) {
        [void]$set.Add($m.Groups[1].Value)
    }
    # 禁管道枚举：把 HashSet 作为单个对象返回（否则退化为字符串数组，SetEquals 不可用）。
    return ,$set
}
$frameworkStage = Join-Path $Root 'Frame\CsharpPrj\Modules\Data\DeclarativeDataflow\RunState\RunStateStage.cs'
if ((Test-Path -LiteralPath $frameworkStage) -and (Test-Path -LiteralPath $contracts)) {
    $frameworkText = [System.IO.File]::ReadAllText($frameworkStage)
    $contractText = [System.IO.File]::ReadAllText($contracts)
    $block = [regex]::Match($contractText, 'class ActorObservationStages(?s:.*?)\r?\n    \}').Value
    if ([string]::IsNullOrWhiteSpace($block)) {
        $violations.Add('[S12] 合同缺 ActorObservationStages 词表（不可判）')
    } else {
        $frameworkSet = Get-StageTokens $frameworkText
        $mirrorSet = Get-StageTokens $block
        # unknown = 观测层「无运行事实」占位（框架词表不含），对拍前剔除。
        [void]$mirrorSet.Remove('unknown')
        if (-not $frameworkSet.SetEquals($mirrorSet)) {
            $violations.Add("[S12] 阶段词表漂移：框架={$($frameworkSet -join ',')} 观测={$($mirrorSet -join ',')}")
        }

        # E-13：Normalize 同义词映射也必须可对拍（不再只看 All 常量集）。
        $mapBlock = [regex]::Match($contractText, 'Map\s*\{.*?\};', 'Singleline').Value
        $expectedMap = @{ 'done' = 'Completed'; 'faulted' = 'Failed'; 'error' = 'Failed'; 'canceled' = 'Cancelled' }
        foreach ($key in $expectedMap.Keys) {
            if ($mapBlock -notmatch "\[`"$key`"\]\s*=\s*$($expectedMap[$key])") {
                $violations.Add("[S12] 阶段同义词映射缺失/漂移：$key ⇒ $($expectedMap[$key])")
            }
        }
    }
} else {
    $violations.Add('[S12] 缺件（判据不可判）：RunStateStage.cs / ActorObservationContracts.cs')
}

# S12b 终态集合单源（F5）：观测侧不得自持终态判定，必须委派框架 RunStateStage.IsTerminal。
if (-not [string]::IsNullOrWhiteSpace($contractText)) {
    $stageBlock = [regex]::Match($contractText, 'class ActorObservationStages(?s:.*?)\r?\n    \}').Value
    if ([string]::IsNullOrWhiteSpace($stageBlock)) {
        $violations.Add('[S12b] 合同缺 ActorObservationStages 块（不可判）')
    } elseif ($stageBlock -notmatch 'RunStateStage\.IsTerminal\(') {
        $violations.Add('[S12b] 终态集合未委派框架 RunStateStage.IsTerminal（观测侧自持终态集合）')
    }

    # 负对照：自持终态集合的合成样例必须被同一判据捕获（防判据空转）。
    $probeSelfOwned = 'public static bool IsTerminal(string? stage) { var value = Normalize(stage); return value == Completed; }'
    if ($probeSelfOwned -match 'RunStateStage\.IsTerminal\(') {
        $violations.Add('[S12b] 负对照失效：自持终态合成样本未被捕获')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ActorObservationSurface'
