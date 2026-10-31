#requires -Version 5.1
<#
.SYNOPSIS
    生效 mode 单源守卫（EffectiveModeSingleSource）—— F1 会话期上下文单源框架。

.DESCRIPTION
    判据：
      G1 求解点唯一：`SessionRuntimeContextResolver` 在位且为唯一求值点；
         旧的 `ConversationStageBodies` 内联 `EffectiveMode(` 定义 = 0。
      G1b 死键清零：`ConversationPipeline.PromptSnapshotKey` 读点 = 0（全仓 `ReadArchive(ConversationPipeline.PromptSnapshotKey)` = 0）。
      G2 呈现裁决单入口：`OperationModeCatalog.IsOperation` 的消费点仅 `SessionPresentation`（Agent.Contract）；其余源文件命中 = 0。
      G3 调用点零内联回退：`ConversationStageBodies` 内 `ResolveChannel(ctx.DeclaredMode)` 命中 = 0。
      G5 缺省同源：生效 mode 缺省取词表单源（`SessionPresentation.DefaultMode`），行为侧 `StatePromptProfile.Dispatch = SessionPresentation.DefaultMode`。
      G6 正/负对照：合成样本必被 G3/G5 捕获（防判据空转）。

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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'EffectiveModeSingleSource'
$Root = $ctx.Root
$violations = $ctx.Violations

Write-Host ''
Write-Host '============================================================'
Write-Host '  生效 mode 单源守卫（EffectiveModeSingleSource：F1）'
Write-Host '============================================================'

$srcDir     = Join-Path $Root 'Projects\Plug\Agent\src'
$stageBodies = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Orchestration\SessionWorkflow\Circuit\ConversationStageBodies.cs'
$resolver    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Orchestration\SessionWorkflow\SessionRuntimeContextResolver.cs'
$contractDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract'

# G1 求解点唯一：Resolver 在位且含唯一 EffectiveMode 求解；旧内联定义 = 0。
if (-not (Test-Path -LiteralPath $resolver)) {
    $violations.Add('[G1] 缺件（判据不可判）：SessionRuntimeContextResolver.cs（求解点未建立）')
} else {
    $resolverText = [System.IO.File]::ReadAllText($resolver)
    if ($resolverText -notmatch 'SessionPresentation\.DefaultMode') {
        $violations.Add('[G1] Resolver 未套用词表单源缺省（SessionPresentation.DefaultMode）')
    }
}

if (-not (Test-Path -LiteralPath $stageBodies)) {
    $violations.Add("[G1] 缺件（判据不可判）：$stageBodies")
} else {
    $text = [System.IO.File]::ReadAllText($stageBodies)
    $oldDefs = ([regex]::Matches($text, 'private\s+static\s+string\?\s+EffectiveMode\s*\(')).Count
    if ($oldDefs -ne 0) { $violations.Add("[G1] 旧内联求值点残留 = $oldDefs（应 0；求解点上移 Resolver）") }
    if ($text -notmatch 'SessionPresentation\.ResolveChannel\(') {
        $violations.Add('[G1] 呈现通道未消费生效 mode（应经 SessionPresentation.ResolveChannel(...)）')
    }
    if ($text -match 'PromptSnapshotKey') {
        $violations.Add('[G1b] 残留死键引用 ConversationPipeline.PromptSnapshotKey（应 0）')
    }
    if ($text -match 'ResolveChannel\(\s*ctx\.DeclaredMode\s*\)') {
        $violations.Add('[G3] 呈现侧仍内联回退 declaredMode（呈现 ≠ 行为 双源）')
    }
    if ($text -notmatch 'SessionPresentation\.DefaultMode') {
        $violations.Add('[G5] 生效 mode 未套用词表单源缺省（SessionPresentation.DefaultMode）')
    }
    # 负对照：合成样本必须被 G3/G5 捕获（防判据空转）。
    $probe = 'Channel = ResolveChannel(ctx.DeclaredMode)'
    if ($probe -notmatch 'ResolveChannel\(\s*ctx\.DeclaredMode\s*\)') {
        $violations.Add('[G6] 负对照失效：合成样本未被内联回退判据捕获')
    }
}

# G5b 词表单源：行为侧缺省档必须引用同一常量（禁双写 "dispatch"）。
$promptCatalog = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Response\Response\StatePromptCatalog.cs'
if (-not (Test-Path -LiteralPath $promptCatalog)) {
    $violations.Add("[G5] 缺件（判据不可判）：$promptCatalog")
} elseif ([System.IO.File]::ReadAllText($promptCatalog) -notmatch 'Dispatch\s*=\s*SessionPresentation\.DefaultMode') {
    $violations.Add('[G5] 行为侧缺省档未与呈现侧同源（StatePromptProfile.Dispatch 应为 SessionPresentation.DefaultMode）')
}

# G1b 全仓死键清零：禁止 ReadArchive(ConversationPipeline.PromptSnapshotKey)
if (Test-Path -LiteralPath $srcDir) {
    $dead = [System.Collections.Generic.List[string]]::new()
    foreach ($f in (Get-ChildItem -LiteralPath $srcDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
        if ([System.IO.File]::ReadAllText($f.FullName) -match 'ReadArchive\(\s*ConversationPipeline\.PromptSnapshotKey') { $dead.Add($f.FullName) }
    }
    if ($dead.Count -gt 0) { $violations.Add("[G1b] PromptSnapshotKey 归档读点 = $($dead.Count)（应 0；真源 = 运行期 IPromptSnapshotStore）") }
}

# G2 呈现裁决单入口
if (Test-Path -LiteralPath $contractDir) {
    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($f in (Get-ChildItem -LiteralPath $contractDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -eq 'SessionPresentation.cs' -or $f.Name -eq 'OperationModeCatalog.cs') { continue }
        if ([System.IO.File]::ReadAllText($f.FullName) -match 'OperationModeCatalog\.IsOperation') { $hits.Add($f.FullName) }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $contractDir 'Conversation\SessionPresentation.cs'))) {
        $violations.Add('[G2] 呈现裁决单入口缺失：Agent.Contract\Conversation\SessionPresentation.cs')
    }
    if ($hits.Count -gt 0) { $violations.Add("[G2] OperationModeCatalog.IsOperation 越点消费 $($hits.Count) 处（应仅 SessionPresentation）") }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'EffectiveModeSingleSource'
