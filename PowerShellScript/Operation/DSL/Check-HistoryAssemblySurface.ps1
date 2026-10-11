#requires -Version 5.1
<#
.SYNOPSIS
    History 装配面守卫（HistoryAssemblySurface）—— 空态预装配 + History 档案分档 + 动态视口单一入口 + 缓存最大化分段。
.DESCRIPTION
    判据（H1–H9，载体=结构事实/源码文本，只读）：
      H1 空态预装配：ConversationViewService 空态分支渲染预装配面（BuildBasePromptSnapshot(profileKey)），非空数组短路。
      H2 档案分档：存在 PromptProfiles.History 消费 + Config/prompt-profiles.ushar 声明面。
      H3 模式注入：History 视图快照注入 mode（HistoryDefaultMode）。
      H4 视口单一入口：TaskRuntimeReportProvider 为 History 侧 task 枚举唯一入口（HistoryTaskRuntimeContext 内无裸调 GetHistoryTaskKeysAsync）。
      H5 端口只读：新增视口实现文件零 ur_* 写命中。
      H6 缓存最大化：HistoryViewportAssembler 提供稳定/易变分段（RenderSegments）+ 差分（RenderDelta）。
      H7 通用框架件：StatePromptCatalog / IDecisionSource / IInjectionPort 在册，HistoryModeCatalog 已通用化删除。
      H8 宿主端口注册：组合根注入 IInjectionPort / ICreateTaskPort。
      H9 注入作用域键：session-history 声明 session_id inport。
      H10 工具面合并：ConversationViewService 两处（空态 + 主路径）合并工具面（WithToolFace + IToolFaceCatalog）。
      H11 自身不持有：history 入站输入标 FeedTarget.State（不驻留本执行体 conversation_context；I2 / §4.6）。
      H12 通用传递框架编排单源：transfer-contribute/transfer-dispatch.ushar 各恰 1 条 [Flow]；TransferFlow 经模板基类转发（无自持编排）。
      H13 通用传递框架词表单源：字段/稳定度/载荷标记 ∈ Config/nodes/transfer.ushar。
      H14 通用传递框架入口注册：组合根注入 ITransferFlow + TransferOpsServices。
    模式：默认报告（不设退出码）；-Check 校验（违规 → 退出码 1）。只读仓库，零侧写。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Projects\Plug\Agent'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    return 'D:\Project\Main'
}

$repoRoot = Find-RepoRoot
$agentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
$violations = New-Object System.Collections.Generic.List[string]

function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }
function Read-Text([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-Content -LiteralPath $Path -Raw) } return '' }

# H1 空态预装配
$view = Join-Path $agentRoot 'src\Agent.Launcher\Modules\Libraries\ConversationViewService.cs'
$vt = Read-Text $view
if ($vt -notmatch 'BuildBasePromptSnapshot\(profileKey') {
    Add-Violation 'H1' 'ConversationViewService 空态未渲染预装配面（缺 BuildBasePromptSnapshot(profileKey)）'
}

# H2 档案分档 + 声明面
$profiles = Join-Path $agentRoot 'src\Agent.Axioms\Prompts\PromptProfiles.cs'
$profileCfg = Join-Path $agentRoot 'Config\prompt-profiles.ushar'
$l0History = Join-Path $agentRoot 'skills\l0\l0-history\SKILL.md'
if (-not (Test-Path -LiteralPath $profiles)) { Add-Violation 'H2' '缺 PromptProfiles.cs' }
if (-not (Test-Path -LiteralPath $profileCfg)) { Add-Violation 'H2' '缺 Config/prompt-profiles.ushar 声明面' }
if ($vt -notmatch 'PromptProfiles\.History') { Add-Violation 'H2' 'ConversationViewService 未按 History 档案分档' }
if (-not (Test-Path -LiteralPath $l0History)) { Add-Violation 'H2' '缺 l0-history DSL 承载技能' }

# H3 模式注入
if ($vt -notmatch 'HistoryDefaultMode') { Add-Violation 'H3' 'History 视图快照未注入 mode（缺 HistoryDefaultMode）' }

# H4 视口单一入口（History 侧 task 枚举唯一 = TaskRuntimeReportProvider）
$provider = Join-Path $agentRoot 'src\Agent.Session\Projection\TaskRuntimeReportProvider.cs'
$context = Join-Path $agentRoot 'src\Agent.Session\Projection\HistoryTaskRuntimeContext.cs'
if (-not (Test-Path -LiteralPath $provider)) { Add-Violation 'H4' '缺 TaskRuntimeReportProvider.cs（通用视口供数口）' }
$ct = Read-Text $context
if ($ct -match 'GetHistoryTaskKeysAsync') { Add-Violation 'H4' 'HistoryTaskRuntimeContext 仍裸调 GetHistoryTaskKeysAsync（应收敛到端口）' }

# H5 端口只读（新增视口实现零 ur_* 写）
foreach ($f in @($provider, (Join-Path $agentRoot 'src\Agent.Session\Projection\HistoryViewportAssembler.cs'))) {
    $ft = Read-Text $f
    if ($ft -match '(?i)\b(INSERT|UPDATE|DELETE)\b[\s\S]{0,40}\bur_') {
        Add-Violation 'H5' "视口实现含写库 SQL：$f"
    }
}

# H6 缓存最大化分段/差分
$asm = Read-Text (Join-Path $agentRoot 'src\Agent.Session\Projection\HistoryViewportAssembler.cs')
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

# H8 宿主端口注册（组合根注入 IInjectionPort / ICreateTaskPort；R2/R3）
$steps = Read-Text (Join-Path $agentRoot 'src\Agent.Launcher\Composition\Circuit\ConversationAssemblySteps.cs')
if ($steps -notmatch 'Register<Agent\.Modules\.Response\.IInjectionPort>') {
    Add-Violation 'H8' '组合根未注入 IInjectionPort（注入动作端口）'
}
if ($steps -notmatch 'Register<Agent\.Modules\.Response\.ICreateTaskPort>') {
    Add-Violation 'H8' '组合根未注入 ICreateTaskPort（建任务端口）'
}
if ($steps -notmatch 'Register<Agent\.Modules\.Response\.IDecisionSource>') {
    Add-Violation 'H8' '组合根未注入 IDecisionSource（决策端口）'
}

# H9 注入作用域键 inport（session-history 声明 session_id；R2）
$sh = Read-Text (Join-Path $agentRoot 'Config\workflows\session-history.ushar')
if ($sh -notmatch 'session_id') { Add-Violation 'H9' 'session-history 未声明 session_id inport（注入作用域键）' }

# H10 工具面合并（History/Task 两视图同源渲染工具面，profile 无关；R3）
$viewSvc = Read-Text (Join-Path $agentRoot 'src\Agent.Launcher\Modules\Libraries\ConversationViewService.cs')
if (([regex]::Matches($viewSvc, 'WithToolFace\(')).Count -lt 2) {
    Add-Violation 'H10' 'ConversationViewService 未在（空态 + 主路径）两处合并工具面（WithToolFace）'
}
if ($viewSvc -notmatch 'IToolFaceCatalog') {
    Add-Violation 'H10' '工具面缺静态装配面直读来源（IToolFaceCatalog）'
}

# H11 自身不持有：history 入站输入标 FeedTarget.State（不驻留本执行体 conversation_context；I2 / 4.6）
$stages = Read-Text (Join-Path $agentRoot 'src\Agent.Orchestration\SessionWorkflow\Circuit\ConversationStageBodies.cs')
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
$tf = Read-Text (Join-Path $agentRoot 'src\Agent.Core\Nodes\Bookkeeping\TransferFlow.cs')
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

if ($violations.Count -eq 0) {
    Write-Host "[HistoryAssemblySurface] OK（H1–H14 全过）"
    exit 0
}

Write-Host "[HistoryAssemblySurface] 违规 $($violations.Count) 项："
foreach ($v in $violations) { Write-Host "  - $v" }
if ($Check) { exit 1 }
exit 0
