#requires -Version 5.1
<#
.SYNOPSIS
    History 装配面守卫（HistoryAssemblySurface）—— 空态预装配 + History 档案分档 + 动态视口单一入口 + 缓存最大化分段。
.DESCRIPTION
    判据（H1–H9，载体=结构事实/源码文本，只读）：
      H1 空态预装配：ConversationViewService 空态分支渲染预装配面（BuildBasePromptSnapshot(profileKey)），非空数组短路。
      H2 档案分档：存在 PromptProfiles.History 消费 + Config/prompt-profiles.ushar 声明面；框架常驻基础段声明化（[Profile] base=on 在册 ∧ FrameworkBaseBands 读声明面 BaseBands）。
      H3 模式注入：History 视图快照注入 mode（HistoryDefaultMode）。
      H4 视口单一入口：TaskRuntimeReportProvider 为 History 侧 task 枚举唯一入口（HistoryTaskRuntimeContext 内无裸调 GetMemberKeysAsync）。
      H5 端口只读：新增视口实现文件零 ur_* 写命中。
      H6 缓存最大化：HistoryViewportAssembler 提供稳定/易变分段（RenderSegments）+ 差分（RenderDelta）。
      H7 通用框架件：StatePromptCatalog / IDecisionSource / IInjectionPort 在册，HistoryModeCatalog 已通用化删除。
      H8 宿主端口注册：组合根注入 IInjectionPort / ICreateTaskPort。
      H9 注入作用域键：session-history 声明 session_id inport。
      H10 工具面分层（C 方案）：ConversationViewService 不再并入工具面（无 WithToolFace/BuildToolFaceBlock）；工具发现归 tool-catalog 子智能体（ToolCatalogGrouper/ToolCatalogCore 在册）。
      H11 自身不持有：history 入站输入标 FeedTarget.State（不驻留本执行体 conversation_context；I2 / §4.6）。
      H12 通用传递框架编排单源：transfer-contribute/transfer-dispatch.ushar 各恰 1 条 [Flow]；TransferFlow 经模板基类转发（无自持编排）。
      H13 通用传递框架词表单源：字段/稳定度/载荷标记 ∈ Config/nodes/transfer.ushar。
      H14 通用传递框架入口注册：组合根注入 ITransferFlow + TransferOpsServices。
      H19 档案段归属（band ownership，声明化）：允许段 = {[Skill].dsl : profiles ∋ 档案}（读 Config/skills.ushar，零白名单）；format 恒首段；l0-common 两档案常驻。
      H20 发送入口单一（S1/S2 聚拢）：会话发送恒经 ISessionSend + SessionSendRequest（Agent 必填）；调用点无裸多实参旧形态。
      H21 决策分层择一（L-D/L-G/L-F）：组合根按网关装配择一注册决策端口；多层决策源在册 ∧ 无自造第二套决策机制。
      H22 决策声明面在册：LLM 判定契约 + 分发决策规则集（含 then.type=llm 与 then.contract）各恰一处。
      H23 标签框架四方对账（缺一即红）：声明面 tags 段 token ⇔ 读/行为消费面（selections has/hasany 或 behaviors 表达式）；
          behaviors 引用的 axis.token 须 ∈ 本域 tags/marks 声明集（未声明 token ⇒ 死代码/漂移）。
      H24 建容器单入口（P1/K1）：IHistoryProvision 实现点 = 1 ∧ 建容器原语直呼点 = 1。
      H25 派生任务键声明化（P2/K4）：task_state_ 字面量单点（仅 DerivedTaskKeyRules.cs；散落即红）。
    模式：默认报告（不设退出码）；-Check 校验（违规 → 退出码 1）。只读仓库，零侧写。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


function Find-RepoRoot {
    $d = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    if ($d) { return $d }
    throw '[HistoryAssemblySurface] 无法定位仓库根（上溯未找到含 Projects\Plug\Agent 的目录）'
}

$repoRoot = Find-RepoRoot
$agentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }
function Read-Text([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-Content -LiteralPath $Path -Raw) } return '' }

# H1 预装配面（R1 拆分后：提示词解析单一入口 = PromptSurfaceResolver；History 走 L0 段）
$viewFiles = @(
    (Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'),
    (Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Board.cs'),
    (Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Nodes.cs'),
    (Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.View.cs'),
    (Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Projection\PromptSurfaceResolver.cs')
)
$vt = (@($viewFiles | ForEach-Object { Read-Text $_ }) -join "`n")
if ($vt -notmatch 'PromptSurfaceResolver') {
    Add-Violation 'H1' '缺提示词面解析单一入口（PromptSurfaceResolver）'
}
if ($vt -notmatch 'ResolvePromptTaskKey') {
    Add-Violation 'H1' 'History 提示词未按选中任务键解析（缺 ResolvePromptTaskKey）'
}

# H2 档案分档 + 声明面
$profiles = Join-Path $agentRoot 'src\Agent.Axioms\Prompts\PromptProfiles.cs'
$profileCatalog = Join-Path $agentRoot 'src\Agent.Axioms\Prompts\PromptProfileCatalog.cs'
$profileCfg = Join-Path $agentRoot 'Config\prompt-profiles.ushar'
$l0History = Join-Path $agentRoot 'skills\l0\storage\l0-history\SKILL.md'
if (-not (Test-Path -LiteralPath $profiles)) { Add-Violation 'H2' '缺 PromptProfiles.cs' }
if (-not (Test-Path -LiteralPath $profileCatalog)) { Add-Violation 'H2' '缺 PromptProfileCatalog.cs' }
if (-not (Test-Path -LiteralPath $profileCfg)) { Add-Violation 'H2' '缺 Config/prompt-profiles.ushar 声明面' }
if ((Read-Text $profileCatalog) -notmatch 'PromptProfiles\.History') { Add-Violation 'H2' 'PromptProfileCatalog 未按 History 档案分档' }
if (-not (Test-Path -LiteralPath $l0History)) { Add-Violation 'H2' '缺 l0-history DSL 承载技能' }
# H2+ 框架常驻基础段声明化（单源）：base=on 声明面在册，且代码侧不硬编码 band 字面量。
$profileCfgText = Read-Text $profileCfg
if ($profileCfgText -notmatch '(?m)^\s*\[Profile\]\s*base\s*:') { Add-Violation 'H2' '缺 [Profile] base 框架常驻基础段声明' }
$profilesText = Read-Text $profiles
if ($profilesText -notmatch 'FrameworkBaseBands\s*=>\s*PromptProfileCatalog\.BaseBands') { Add-Violation 'H2' 'FrameworkBaseBands 未读声明面（应 = PromptProfileCatalog.BaseBands）' }

# H3 L0 面单一来源（History 只读 L0 段；禁对话轮次进入 Prompt 面）
if ($vt -notmatch 'L0Segments') { Add-Violation 'H3' 'History 未经 L0Segments 过滤（禁对话轮次进入 Prompt 面）' }
if ($vt -notmatch 'text_prompt_history_no_task' -and $vt -notmatch 'PromptNoTask') { Add-Violation 'H3' 'History 提示词空态未走专用归因键' }

# H4 视口单一入口（History 侧 task 枚举唯一 = TaskRuntimeReportProvider）
$provider = Join-Path $agentRoot 'src\Agent.Session\Projection\History\TaskRuntimeReportProvider.cs'
$context = Join-Path $agentRoot 'src\Agent.Session\Projection\History\HistoryTaskRuntimeContext.cs'
if (-not (Test-Path -LiteralPath $provider)) { Add-Violation 'H4' '缺 TaskRuntimeReportProvider.cs（通用视口供数口）' }
$ct = Read-Text $context
if ($ct -match 'GetMemberKeysAsync') { Add-Violation 'H4' 'HistoryTaskRuntimeContext 仍裸调 GetMemberKeysAsync（应收敛到端口）' }

# H5 端口只读（新增视口实现零 ur_* 写）
foreach ($f in @($provider, (Join-Path $agentRoot 'src\Agent.Session\Projection\History\HistoryViewportAssembler.cs'))) {
    $ft = Read-Text $f
    if ($ft -match '(?i)\b(INSERT|UPDATE|DELETE)\b[\s\S]{0,40}\bur_') {
        Add-Violation 'H5' "视口实现含写库 SQL：$f"
    }
}

# H6 缓存最大化分段/差分
$asm = Read-Text (Join-Path $agentRoot 'src\Agent.Session\Projection\History\HistoryViewportAssembler.cs')
if ($asm -notmatch 'RenderSegments') { Add-Violation 'H6' 'HistoryViewportAssembler 缺 RenderSegments（稳定/易变分段）' }
if ($asm -notmatch 'RenderDelta') { Add-Violation 'H6' 'HistoryViewportAssembler 缺 RenderDelta（差分渲染）' }

# H7 通用状态提示词框架件（模式表通用化 + 决策/注入端口抽象；R0/R1）
$respDir = Join-Path $agentRoot 'src\Agent.Modules\Response\Response'
foreach ($f in @('StatePromptCatalog.cs', 'IDecisionSource.cs', 'IInjectionPort.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $respDir $f))) { Add-Violation 'H7' "缺通用状态提示词框架件：$f" }
}
if (Test-Path -LiteralPath (Join-Path $respDir 'HistoryModeCatalog.cs')) {
    Add-Violation 'H7' 'HistoryModeCatalog.cs 未通用化（应删，改 StatePromptCatalog）'
}

# H8 宿主端口注册（组合根注入 IInjectionPort / IDecisionSource；R2/R3）
$steps = Read-Text (Join-Path $agentRoot 'src\Agent.Launcher\Composition\Hosting\Session\ConversationAssemblySteps.cs')
if ($steps -notmatch 'Register<Agent\.Modules\.Response\.IInjectionPort>') {
    Add-Violation 'H8' '组合根未注入 IInjectionPort（注入动作端口）'
}
if ($steps -notmatch 'Register<Agent\.Modules\.Response\.IDecisionSource>') {
    Add-Violation 'H8' '组合根未注入 IDecisionSource（决策端口）'
}

# H9 注入作用域键 inport（session-history 声明 session_id；R2）
$sh = Read-Text (Join-Path $agentRoot 'Config\workflows\session-history.ushar')
if ($sh -notmatch 'session_id') { Add-Violation 'H9' 'session-history 未声明 session_id inport（注入作用域键）' }

# H10 工具面分层（C 方案）：基础提示词层零工具清单平铺；工具发现归 tool-catalog 子智能体（分类分级 + 按需展开）
$viewSvc = Read-Text (Join-Path $agentRoot 'src\Agent.Launcher\Modules\Libraries\ConversationViewService.cs')
if ($viewSvc -match 'WithToolFace\s*\(') {
    Add-Violation 'H10' 'ConversationViewService 仍将工具面并入基础提示词层（WithToolFace 应移除）'
}
if ($viewSvc -match 'BuildToolFaceBlock\s*\(') {
    Add-Violation 'H10' 'ConversationViewService 仍平铺工具面块（BuildToolFaceBlock 应移除）'
}
$subAgentHosting = Read-Text (Join-Path $agentRoot 'src\Agent.Modules\SubAgent\Runtime\SubAgentHosting.cs')
if ($subAgentHosting -notmatch 'ToolCatalogSubAgentTools') {
    Add-Violation 'H10' '缺 tool-catalog 子智能体工具装配（ToolCatalogSubAgentTools 未注册）'
}
if (-not (Test-Path -LiteralPath (Join-Path $agentRoot 'src\Agent.Modules\SubAgent\Tools\ToolCatalogGrouper.cs'))) {
    Add-Violation 'H10' '缺工具分级分组器（ToolCatalogGrouper.cs，分类分级单一实现）'
}
if (-not (Test-Path -LiteralPath (Join-Path $agentRoot 'src\Agent.Core\Operation\Tool\ToolCatalogCore.cs'))) {
    Add-Violation 'H10' '缺工具核心级判定表（ToolCatalogCore.cs，Core 单一真源）'
}

# H11 自身不持有：history 入站输入标 FeedTarget.State（不驻留本执行体 conversation_context；I2 / 4.6）
$stages = Read-Text (Join-Path $agentRoot 'src\Agent.Core\Orchestration\SessionWorkflow\Circuit\ConversationStageBodies.cs')
if ($stages -notmatch 'IsHistoryAgent') {
    Add-Violation 'H11' 'ConversationStageBodies 缺 history 执行体判定（IsHistoryAgent）'
}
if ($stages -notmatch 'state\.in\.') {
    Add-Violation 'H11' 'history 入站输入未标 FeedTarget.State（自身不持有输入原文，I2）'
}

# H12 通用传递框架编排单源（TransferF）：两条声明流各恰 1 条 [Flow]；TransferFlow 经模板基类转发（无自持编排/无自建执行器）
$tcPath = Join-Path $agentRoot 'Config\workflows\transfer-contribute.ushar'
$tdPath = Join-Path $agentRoot 'Config\workflows\transfer-dispatch.ushar'
if (-not (Test-Path -LiteralPath $tcPath)) { Add-Violation 'H12' '缺 transfer-contribute.ushar（上行贡献编排真源）' }
if (-not (Test-Path -LiteralPath $tdPath)) { Add-Violation 'H12' '缺 transfer-dispatch.ushar（下行分发编排真源）' }
if (([regex]::Matches((Read-Text $tcPath), '(?m)^\[Flow\]')).Count -ne 1) { Add-Violation 'H12' 'transfer-contribute.ushar 须恰 1 条 [Flow]（编排单源）' }
if (([regex]::Matches((Read-Text $tdPath), '(?m)^\[Flow\]')).Count -ne 1) { Add-Violation 'H12' 'transfer-dispatch.ushar 须恰 1 条 [Flow]（编排单源）' }
$tf = Read-Text (Join-Path $agentRoot 'src\Agent.Core\Engine\Nodes\Bookkeeping\TransferFlow.cs')
if ($tf -notmatch 'DeclaredFlowDelegateBase') { Add-Violation 'H12' 'TransferFlow 未经模板基类转发（应 DeclaredFlowDelegateBase，不自持编排）' }
if ($tf -match 'new OneShotFlowRunner\(') { Add-Violation 'H12' 'TransferFlow 自建执行器（编排绕过 BookkeepingFlowAssembly 单源）' }

# H13 通用传递框架词表单源（TransferF）：字段/稳定度/载荷标记 ∈ Config/nodes/transfer.ushar
$tvPath = Join-Path $agentRoot 'Config\nodes\transfer.ushar'
if (-not (Test-Path -LiteralPath $tvPath)) { Add-Violation 'H13' '缺 Config/nodes/transfer.ushar（字段/稳定度/载荷标记词表真源）' }
$tv = Read-Text $tvPath
foreach ($space in @('TransferFieldClass', 'TransferVolatility', 'TransferPayloadFlag')) {
    if ($tv -notmatch [regex]::Escape($space)) { Add-Violation 'H13' "transfer.ushar 缺词表 $space" }
}

# H14 通用传递框架入口注册（TransferF）：组合根注入 ITransferFlow + TransferOpsServices
if ($steps -notmatch 'ITransferFlow') { Add-Violation 'H14' '组合根未注入 ITransferFlow（传递框架唯一入口）' }
if ($steps -notmatch 'TransferOpsServices') { Add-Violation 'H14' '组合根未装配 TransferOpsServices（fn 依赖束）' }

# H15 UI 轨零 Binding（信息状态不得进 wire）：UI 投影器零 Binding；面板经模板渲染（非 ConversationMessage）
$uiProj = Join-Path $agentRoot 'src\Agent.Session\Projection\History\HistoryTaskRuntimeUiProjector.cs'
if (-not (Test-Path -LiteralPath $uiProj)) { Add-Violation 'H15' '缺 HistoryTaskRuntimeUiProjector.cs（UI 投影器）' }
elseif ((Read-Text $uiProj) -match '\.Binding\b|ConversationMessage') { Add-Violation 'H15' 'UI 投影器出现 Binding/ConversationMessage（任务池信息状态不得进 LLM wire）' }

# H16 双出口同供数（IHistoryTaskRuntimeSource）：UI/装配两侧均引用同一供数口；UI 侧不再自建 provider
$srcPath = Join-Path $agentRoot 'src\Agent.Session\Projection\History\HistoryTaskRuntimeSource.cs'
if (-not (Test-Path -LiteralPath $srcPath)) { Add-Violation 'H16' '缺 HistoryTaskRuntimeSource.cs（唯一供数口）' }
if ($steps -notmatch 'IHistoryTaskRuntimeSource') { Add-Violation 'H16' '组合根未引用 IHistoryTaskRuntimeSource（LLM 轨应经供数口）' }
$vt2 = $vt
if ($vt2 -notmatch 'IHistoryTaskRuntimeSource') { Add-Violation 'H16' 'UI 侧未引用 IHistoryTaskRuntimeSource（应经供数口）' }
if ($vt2 -match 'new TaskRuntimeReportProvider\(') { Add-Violation 'H16' 'UI 侧仍自建 TaskRuntimeReportProvider（应经唯一供数口）' }

# H17 空态可见化（空 ≠ 无）：UI 产 taskRuntimeEmpty 模板 ∧ LLM 投影器产 (empty) 标记
$promptProj = Join-Path $agentRoot 'src\Agent.Session\Projection\History\HistoryTaskRuntimePromptProjector.cs'
if (-not (Test-Path -LiteralPath $promptProj)) { Add-Violation 'H17' '缺 HistoryTaskRuntimePromptProjector.cs（LLM 投影器）' }
elseif ((Read-Text $promptProj) -notmatch '\(empty\)') { Add-Violation 'H17' 'LLM 投影器缺空标记 (empty)（空挂载不可静默省略）' }
$uiCfg = Read-Text (Join-Path $repoRoot 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json')
if ($uiCfg -notmatch 'taskRuntimeEmpty') { Add-Violation 'H17' 'AgentRuntimeUI.json 缺 taskRuntimeEmpty 模板（UI 空态占位）' }

# H18 任务池状态单源（TaskExecutionStatus）：状态只经 GetStatusAsync 读取，禁自拼状态字面量
$hostSrc = Read-Text (Join-Path $agentRoot 'src\Agent.Ui\Core\Host\HostHistoryTaskRuntimeSource.cs')
if ($hostSrc -notmatch 'GetStatusAsync') { Add-Violation 'H18' '宿主供数适配器未经 GetStatusAsync 读任务池状态（单源）' }
if ((Read-Text $srcPath) -notmatch 'poolStatus') { Add-Violation 'H18' '供数口缺 poolStatus 注入（任务池状态应经委托单源）' }

# H19 档案段归属（band ownership，声明化）：task/history 允许段 = {[Skill].dsl : profiles ∋ 档案}；format 恒首段。
#  段归属单一聚拢点 = Config/skills.ushar 的 [Skill] profiles= 字段（零 band 白名单硬编码）。
$skillsCfg = Join-Path $agentRoot 'Config\skills.ushar'
if (-not (Test-Path -LiteralPath $skillsCfg)) { Add-Violation 'H19' '缺 Config/skills.ushar 段声明面' }
$skillsCfgText = Read-Text $skillsCfg
# dsl → profiles 集（段归属派生源）
$bandProfiles = @{}
foreach ($line in ($skillsCfgText -split "`r?`n")) {
    $t = $line.Trim()
    if ($t -notmatch '^\[Skill\]\s*(\S+)\s*:\s*(.*)$') { continue }
    $fields = $Matches[2]
    if ($fields -notmatch '(?:^|\|)\s*dsl=([^|]+)') { continue }
    $dsl = $Matches[1].Trim()
    $profiles = @()
    if ($fields -match '(?:^|\|)\s*profiles=([^|]+)') {
        $profiles = @(($Matches[1].Trim() -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    $bandProfiles[$dsl] = $profiles
}
$taskAllow = @($bandProfiles.Keys | Where-Object { $bandProfiles[$_] -contains 'task' })
$historyAllow = @($bandProfiles.Keys | Where-Object { $bandProfiles[$_] -contains 'history' })
$taskBands = $null
$historyBands = $null
foreach ($line in ($profileCfgText -split "`r?`n")) {
    $t = $line.Trim()
    if ($t -match '^\[Profile\]\s*task\s*:\s*bands=([^|]+)') {
        $taskBands = @(($Matches[1].Trim() -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    elseif ($t -match '^\[Profile\]\s*history\s*:\s*bands=([^|]+)') {
        $historyBands = @(($Matches[1].Trim() -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
}
if (-not $taskBands) { Add-Violation 'H19' 'task 档案缺 bands 声明' }
if (-not $historyBands) { Add-Violation 'H19' 'history 档案缺 bands 声明' }
if ($taskBands -and $taskBands[0] -ne 'format') { Add-Violation 'H19' 'task 首段须为 format（FormatInstructionLocality）' }
if ($historyBands -and $historyBands[0] -ne 'format') { Add-Violation 'H19' 'history 首段须为 format（FormatInstructionLocality）' }
foreach ($b in $taskBands) {
    if ($taskAllow -notcontains $b) { Add-Violation 'H19' "task 越档段：$b（不在 profiles∋task 的段声明集内）" }
}
foreach ($b in $historyBands) {
    if ($historyAllow -notcontains $b) { Add-Violation 'H19' "history 越档段：$b（不在 profiles∋history 的段声明集内）" }
}
foreach ($b in @('l0-system', 'l0-loop', 'l0-logic')) {
    if ($historyBands -contains $b) { Add-Violation 'H19' "history 混入 worker 施工段：$b" }
}
foreach ($b in @('l1-region-read', 'l0-orchestration')) {
    if ($taskBands -contains $b) { Add-Violation 'H19' "task 混入容器/主循环段：$b" }
}
if ($taskBands -notcontains 'l0-common' -or $historyBands -notcontains 'l0-common') {
    Add-Violation 'H19' 'l0-common 须同时常驻 task 与 history（framework-base 通用不变式单源）'
}

# H21 决策分层择一（L-D/L-G/L-F）：组合根按「宿主 LLM 网关已装配」择一注册决策端口；
#     多层决策源在册（规则集 + LLM 提议执行）∧ 无自造第二套决策机制（v1.1 IDispatchDecisionJudge 已废弃）。
$conv = $steps
if ($conv -notmatch 'HostDispatchProposalExecutor') {
    Add-Violation 'H21' '组合根未分层择一注册决策端口（缺 HostDispatchProposalExecutor 择一）'
}
if ($conv -notmatch 'StrategyDecisionSource') {
    Add-Violation 'H21' '组合根未装配多层决策源 StrategyDecisionSource（L-D/L-G/L-F）'
}
if ($conv -match 'IDispatchDecisionJudge') {
    Add-Violation 'H21' '组合根仍引用自造决策裁决件 IDispatchDecisionJudge（应复用 Strategy.DecisionRules）'
}
# H22 决策声明面在册（合同 / 规则集 / 提示词正文）：契约与规则集各恰一处声明，正文外置。
$dpContract = Join-Path $agentRoot 'Config\llm\history-dispatch.ushar'
$dpRules = Join-Path $agentRoot 'Config\rules\history-dispatch.rules.ushar'
if (-not (Test-Path -LiteralPath $dpContract)) {
    Add-Violation 'H22' '缺 LLM 判定契约声明面 Config/llm/history-dispatch.ushar'
}
if (-not (Test-Path -LiteralPath $dpRules)) {
    Add-Violation 'H22' '缺分发决策规则集声明面 Config/rules/history-dispatch.rules.ushar'
}
if ((Read-Text $dpRules) -notmatch 'then\.type\s*=\s*llm') {
    Add-Violation 'H22' '规则集未声明 LLM 决策规则（then.type = llm）'
}
if ((Read-Text $dpRules) -notmatch 'then\.contract\s*=\s*HistoryDispatch') {
    Add-Violation 'H22' '规则集未挂判定契约（then.contract = HistoryDispatch）'
}

# H20 发送入口单一（S1/S2）：唯一原语 ISessionSend（Agent 必填）；会话调用点不得残留裸多实参旧形态。
$sendContract = Join-Path $agentRoot 'src\Agent.Session\Contracts\ISessionSend.cs'
$sendContractText = Read-Text $sendContract
if (-not (Test-Path -LiteralPath $sendContract)) {
    Add-Violation 'H20' '缺 ISessionSend 唯一发送原语契约（Agent.Session/Contracts/ISessionSend.cs）'
}
else {
    if ($sendContractText -notmatch 'interface\s+ISessionSend') { Add-Violation 'H20' 'ISessionSend 契约缺接口声明' }
    if ($sendContractText -notmatch 'string\s+Agent') { Add-Violation 'H20' 'ISessionSend 契约缺非空 Agent 字段（发送原语未契约闭合）' }
}
$sessionSvc = Join-Path $agentRoot 'src\Agent.Core\Orchestration\SessionWorkflow\ConversationSessionService.cs'
if ((Read-Text $sessionSvc) -notmatch 'ISessionSend') {
    Add-Violation 'H20' 'ConversationSessionService 未实现 ISessionSend（未收敛为唯一实现）'
}
$sendCallSites = @(
    'src\Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs',
    'src\Agent.Ui\Core\Host\AgentHistoryRuntimePort.cs',
    'src\Agent.Ui\Core\Governance\AgentRoundBodyRunner.cs',
    'src\Agent.Launcher\Composition\Ports\ResponsePorts.cs',
    'src\Agent.Launcher\Composition\Hosting\Session\ConversationAssemblySteps.cs')
foreach ($rel in $sendCallSites) {
    if ((Read-Text (Join-Path $agentRoot $rel)) -match 'SendAsync\(\s*[A-Za-z_]\w*\s*,\s*[A-Za-z_]\w*\s*,\s*[A-Za-z_]\w*\s*\)') {
        Add-Violation 'H20' "$rel 仍存在裸多实参会话发送（未经 SessionSendRequest）"
    }
}

# H23 标签框架四方对账：声明 tags token ⇔ 读/行为消费 ⇔ 声明集闭环。
$nodesDir = Join-Path $agentRoot 'Config\nodes'
if (Test-Path -LiteralPath $nodesDir) {
    $sectionHeaders = @('marks', 'selections', 'behaviors', 'origins', 'retentions', 'placements', 'identity', 'context', 'sequence', 'tags')
    foreach ($file in (Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar')) {
        $text = Read-Text $file.FullName
        if ($text -notmatch '(?m)^\s*tags\s*$') { continue }

        $inTags = $false
        $tokens = @()
        $readerLines = @()
        foreach ($line in ($text -split "`r?`n")) {
            $t = $line.Trim()
            if ($t -eq 'tags') { $inTags = $true; continue }
            if ($inTags -and ($sectionHeaders -contains $t) -and $t -ne 'tags') { $inTags = $false }
            if ($inTags) {
                if ($t -match '^([A-Za-z0-9_.-]+)\s*=') { $tokens += $Matches[1] }
                continue
            }
            $readerLines += $line
        }
        if ($tokens.Count -eq 0) { continue }
        # 读/行为消费面 = 去掉 tags 声明段后的正文（selection 表达式 / behaviors 表达式均在其内）。
        $readerText = ($readerLines -join "`n")

        foreach ($tok in $tokens) {
            if ($readerText -match [regex]::Escape($tok)) { continue }
            Add-Violation 'H23' "$($file.Name) 声明的 tag token '$tok' 无读/行为消费（selection 'has'/'hasany' 或 behavior 表达式均未引用）"
        }
    }
}

# H23b 声明面 marks 闭合（D5/D7）：marks 段 token 须为内置历史轴缺省 ∨ 已在同域 tags 段声明。
$builtinMarks = @('None', 'Numbered', 'Terminal', 'Port')
if (Test-Path -LiteralPath $nodesDir) {
    foreach ($file in (Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar')) {
        $text = Read-Text $file.FullName
        $marks = @()
        $tags = @()
        $inMarks = $false
        $inTags = $false
        foreach ($line in ($text -split "`r?`n")) {
            $t = $line.Trim()
            if ($t -eq 'marks') { $inMarks = $true; $inTags = $false; continue }
            if ($t -eq 'tags') { $inTags = $true; $inMarks = $false; continue }
            if ($sectionHeaders -contains $t) { $inMarks = $false; $inTags = $false; continue }
            if ($inMarks -and ($t -match '^([A-Za-z0-9_.-]+)\s*=')) { $marks += $Matches[1] }
            if ($inTags -and ($t -match '^([A-Za-z0-9_.-]+)\s*=')) { $tags += $Matches[1] }
        }
        foreach ($m in $marks) {
            if ($builtinMarks -contains $m) { continue }
            if ($tags -contains $m) { continue }
            Add-Violation 'H23' "$($file.Name) marks token '$m' 既非内置缺省也未在 tags 声明（词表真源不闭合）"
        }
    }
}

# H24 建容器单入口（P1/K1）：实现点 = 1 ∧ 入口登记点 = 1
$agentSrc = Join-Path $agentRoot 'src'
$agentCs = @(Get-ChildItem -LiteralPath $agentSrc -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
$ensureImpls = @($agentCs | Select-String -Pattern 'private static async Task<object> EnsureContainerAsync' -List)
if ($ensureImpls.Count -ne 1) {
    Add-Violation 'H24' "建容器实现点=$($ensureImpls.Count)（应为 1；建容器须收敛为单一所有者）"
}
$ensureRegister = @($agentCs | Select-String -Pattern 'WorkflowFunctionNames\.EnsureHistory' -List)
if ($ensureRegister.Count -ne 1) {
    Add-Violation 'H24' "建容器入口登记点=$($ensureRegister.Count)（应为 1；多入口即多写面）"
}

# H25 派生任务键声明化（P2/K4）：C# 字面量唯一允许点 = AgentKeySpaceDeclaration.cs（声明单源）；
#     DerivedTaskKeyRules / MemberKeyPolicy 等一律转发；JSON 声明面必须同时登记。
$derivedFiles = @($agentCs | Select-String -Pattern 'task_state_|task_member_' | ForEach-Object { $_.Path } | Sort-Object -Unique)
$disallowedDerived = @($derivedFiles | Where-Object { $_ -notmatch 'AgentKeySpaceDeclaration\.cs$' })
if ($disallowedDerived.Count -gt 0) {
    Add-Violation 'H25' "派生任务前缀字面量散落 $($disallowedDerived.Count) 个非声明单源文件：$($disallowedDerived -join ', ')"
}
$keyspaceJson = Join-Path $agentRoot 'Config\keyspaces\agent.keyspace.generated.json'
$jsonText = Read-Text $keyspaceJson
if ($jsonText -notmatch 'task_state_' -or $jsonText -notmatch 'task_member_') {
    Add-Violation 'H25' 'agent.keyspace.generated.json 未登记派生任务前缀（task_member_ / task_state_）'
}

# H26–H30 History 读取链彻底重构（单读面 / 单原因码 / 单开关 / 单取形 / 浮层作用域）
$boardFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Board.cs'
$boardText = Read-Text $boardFile
if ($boardText -match 'GetHistoryAsync' -or $boardText -match 'IHistoryTaskRuntimeSource') {
    Add-Violation 'H26' 'History 板仍直读领域（Board.cs 命中 GetHistoryAsync/IHistoryTaskRuntimeSource；应只消费 history.board 读模型）'
}
if ($boardText -notmatch 'HistoryBoardReadModelProjector') {
    Add-Violation 'H26' 'History 板缺读模型回退装配单点（HistoryBoardReadModelProjector）'
}

$boardModelFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Projection\HistoryBoardModel.cs'
$boardModelText = Read-Text $boardModelFile
foreach ($reasonField in @('CardsEmptyReason', 'FeedEmptyReason', 'ActorsEmptyReason')) {
    if ($boardModelText -notmatch [regex]::Escape($reasonField)) {
        Add-Violation 'H27' "HistoryBoardModel 缺原因码字段 $reasonField（空态须带空因）"
    }
}
$rendererFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Rendering\ConversationPageRenderer.cs'
$rendererText = Read-Text $rendererFile
if ($rendererText -notmatch 'UiReasonCatalog') {
    Add-Violation 'H27' '渲染层未消费 UiReasonCatalog（文案映射单点缺失）'
}

$promptResolverFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Projection\PromptSurfaceResolver.cs'
$promptResolverText = Read-Text $promptResolverFile
if ($promptResolverText -notmatch 'Origin') {
    Add-Violation 'H28' 'PromptSurface 缺 Origin 来源标记'
}
if ($rendererText -notmatch 'text_prompt_origin') {
    Add-Violation 'H28' '渲染层未渲染提示词来源（text_prompt_origin 无消费）'
}

$overlayFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Overlay.cs'
$overlayText = Read-Text $overlayFile
if ($overlayText -match '_overlayByKey' -or $overlayText -match '_overlayHost') {
    Add-Violation 'H35' '浮层仍用领域自建字典/裸宿主（须经 UiOverlayRegistry 单源分槽）'
}
if ($overlayText -notmatch 'UiOverlayRegistry') {
    Add-Violation 'H35' '浮层未使用 UiOverlayRegistry（视图 × 类型 注册表缺失）'
}

# H31 Board 不得直读窗口端口（窗口只经 history.board 读模型）
if ($boardText -match 'ReadWindowAsync') {
    Add-Violation 'H31' 'History 板直读窗口端口（Board.cs 命中 ReadWindowAsync；须经读模型）'
}

# H32 卡片窗口必须经 WindowContext 取形（禁 Full + Take 二次裁剪）
$projectorText = Read-Text (Join-Path $agentRoot 'src\Agent.Ui\Core\Projection\HistoryBoardReadModelProjector.cs')
if ($projectorText -notmatch 'UiReadShape\.WindowContext') {
    Add-Violation 'H32' '卡片窗口未使用 UiReadShape.WindowContext（禁 Full+Take 二次裁剪）'
}

# H33 窗口取形声明单源：有装载/消费点，且适配器无词表字面量
$windowPolicyFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Projection\WindowContextPolicy.cs'
$windowPolicyText = Read-Text $windowPolicyFile
if ($windowPolicyText -match '"(endpoint|heartbeat|llm-call|llm_call|stage|tool)"') {
    Add-Violation 'H33' 'WindowContextPolicy 仍硬编码阶段/角色词表（须读 read-paths.json windowContext）'
}
$windowDeclFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Projection\UiWindowDeclaration.cs'
if (-not (Test-Path -LiteralPath $windowDeclFile)) {
    Add-Violation 'H33' '缺窗口声明装载点（UiWindowDeclaration）'
}
$readPathsFile = Join-Path $agentRoot 'Config\nodes\read-paths.json'
if ((Read-Text $readPathsFile) -notmatch 'windowContext') {
    Add-Violation 'H33' 'read-paths.json 未声明 windowContext（窗口取形无单源）'
}

# H34 选择写入必须显式视图参数（禁 ambient 作用域）
$selectionFile = Join-Path $agentRoot 'src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.Selection.cs'
$selectionText = Read-Text $selectionFile
if ($selectionText -notmatch 'SetCurrentTask\(MainDialogViewType') {
    Add-Violation 'H34' 'SetCurrentTask 未显式声明视图参数（ambient 作用域写）'
}
if ($selectionText -notmatch 'RememberTask\(MainDialogViewType') {
    Add-Violation 'H34' 'RememberTask 未显式声明视图参数（ambient 作用域写）'
}

# H36 提示词陈旧原因码必须有生产点（或删除该码）
if ($promptResolverText -notmatch 'PromptHistoryStale') {
    Add-Violation 'H36' 'PromptHistoryStale 无生产点（历史快照缺失被误报为「未装配」）'
}

# H37 未知运行态原因不得伪装业务空（默认分支只允许显式未知）
$boardProjFile = Join-Path $agentRoot 'src\Agent.Ui\Core\Projection\HistoryBoardReadModelProjector.cs'
$boardProjText = Read-Text $boardProjFile
if ($boardProjText -notmatch 'CardsUnknown') {
    Add-Violation 'H37' '运行态原因默认分支缺显式未知码（CardsUnknown）'
}
if ($boardProjText -match '_ => UiReasonCodes\.CardsEmpty') {
    Add-Violation 'H37' '运行态原因默认分支仍映射业务空（未知伪装成「无任务」）'
}

# H40 领域不得直呼 UiWindowContextPolicy（机制层必须经 WindowContextPolicy 适配）
$policyHits = @(Get-ChildItem -LiteralPath (Join-Path $agentRoot 'src') -Recurse -Filter '*.cs' |
    Select-String -Pattern 'UiWindowContextPolicy\.' -List |
    Where-Object { $_.Path -notmatch 'WindowContextPolicy\.cs$' })
if ($policyHits.Count -gt 0) {
    Add-Violation 'H40' ("领域直呼 UiWindowContextPolicy：" + (($policyHits | ForEach-Object { $_.Path }) -join ', '))
}

if ($violations.Count -eq 0) {
    Write-Host "[HistoryAssemblySurface] OK（H1–H40 全过）"
    exit 0
}

Write-Host "[HistoryAssemblySurface] 违规 $($violations.Count) 项："
foreach ($v in $violations) { Write-Host "  - $v" }
if ($Check) { exit 1 }
exit 0
