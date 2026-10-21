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
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$agent = Join-Path $Root 'Projects\Plug\Agent\src'
$views = Join-Path $agent 'Agent.Ui\Ui\Views'
$required = @(
    'Agent.Session\Projection\ActorObservationContracts.cs',
    'Agent.Session\Projection\ActorInvocationProjector.cs',
    'Agent.Session\Projection\ActorInvocationLedger.cs',
    'Agent.Session\Projection\ActorObservationProjector.cs',
    'Agent.Session\Projection\ActorObservationHub.cs',
    'Agent.Ui\Ui\Views\ActorObservationViewProjector.cs'
)
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force

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

$viewService = Join-Path $views 'ConversationViewService.cs'
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

$assembly = Join-Path $agent 'Agent.Launcher\Composition\Circuit\ConversationAssemblySteps.cs'
if (Test-Path -LiteralPath $assembly) {
    $text = [System.IO.File]::ReadAllText($assembly)
    if ($text -notmatch 'ActorApplicability\.For') { $violations.Add('[S8] 组合根未接 ActorApplicability.For（适用性单源断链）') }
} else {
    $violations.Add('[S8] 缺件（判据不可判）：ConversationAssemblySteps.cs')
}

$viewProjector = Join-Path $views 'ActorObservationViewProjector.cs'
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

$renderer = Join-Path $views 'ConversationPageRenderer.cs'
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
if ((Test-Path -LiteralPath $contracts) -and (Test-Path -LiteralPath $projector)) {
    $contractText = [System.IO.File]::ReadAllText($contracts)
    $projectorText = [System.IO.File]::ReadAllText($projector)
    if ($contractText -notmatch 'ActorInvocationKey') { $violations.Add('[S7] 合同缺 ActorInvocationKey（调用身份未建模）') }
    if ($projectorText -notmatch 'IReadOnlyList<ActorInvocation>') { $violations.Add('[S7] 合并器未以调用实例为输入（声明先行未消除）') }
    if ($projectorText -match 'rows\[key\]\s*=\s*new\s+ActorObservationRow\s*\(') { $violations.Add('[S7] 合并器仍按声明键直接建行（ANTI-DECLARATION-AS-ACTOR）') }
}

$probeDeclaration = 'foreach (var declaration in declarations) { rows[key] = new ActorObservationRow(Id, Kind, true, Stage, Origin); }'
if ($probeDeclaration -notmatch 'rows\[key\]\s*=\s*new\s+ActorObservationRow\s*\(') {
    $violations.Add('[S10] 负对照失效：声明先行合成样本未被 S7 捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] S1–S10 通过（调用实例驱动 + 层级过滤 + 身份纯度 + 零控制面）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0

