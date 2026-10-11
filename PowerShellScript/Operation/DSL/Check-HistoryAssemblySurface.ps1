#requires -Version 5.1
<#
.SYNOPSIS
    History 装配面守卫（HistoryAssemblySurface）—— 空态预装配 + History 档案分档 + 动态视口单一入口 + 缓存最大化分段。
.DESCRIPTION
    判据（H1–H6，载体=结构事实/源码文本，只读）：
      H1 空态预装配：ConversationViewService 空态分支渲染预装配面（BuildBasePromptSnapshot(profileKey)），非空数组短路。
      H2 档案分档：存在 PromptProfiles.History 消费 + Config/prompt-profiles.ushar 声明面。
      H3 模式注入：History 视图快照注入 mode（HistoryDefaultMode）。
      H4 视口单一入口：TaskRuntimeReportProvider 为 History 侧 task 枚举唯一入口（HistoryTaskRuntimeContext 内无裸调 GetHistoryTaskKeysAsync）。
      H5 端口只读：新增视口实现文件零 ur_* 写命中。
      H6 缓存最大化：HistoryViewportAssembler 提供稳定/易变分段（RenderSegments）+ 差分（RenderDelta）。
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

if ($violations.Count -eq 0) {
    Write-Host "[HistoryAssemblySurface] OK（H1–H6 全过）"
    exit 0
}

Write-Host "[HistoryAssemblySurface] 违规 $($violations.Count) 项："
foreach ($v in $violations) { Write-Host "  - $v" }
if ($Check) { exit 1 }
exit 0
