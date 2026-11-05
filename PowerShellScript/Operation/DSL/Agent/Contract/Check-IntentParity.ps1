#requires -Version 5.1
<#
.SYNOPSIS
    意图执行框架（IEF）写读同源对账门禁。

.DESCRIPTION
    只读源码结构事实，不运行测试、不写盘。检查项：
      [P1] 执行注册表存在且 history.link 声明写面 = graph:history_owns_task，读面绑定同一写面；
      [P2] 后端 history.link 必须调用注入写原语 _attachHistoryTask，且不得存在
           「task 未建 → return true」的 no-op 成功；
      [P3] 组合入口必须由执行注册表探针裁决能力位（CreateDefault），且 handler 仍来自 Core 单一实现；
      [P4] 正对照：注册表包含 task.delete 行（防表为空导致判据恒真）。

.PARAMETER Root
    仓库根（默认上溯五级）。

.PARAMETER Check
    校验模式：违规 > 0 退出码 1；默认报告模式恒 0。
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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'IntentParity'
$Root = $ctx.Root
$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
$registry = Join-Path $agentSrc 'Agent.Modules\Response\Intent\Registry\IntentExecutionRegistry.cs'
$backend  = Join-Path $agentSrc 'Agent.Core\Entry\Handlers\AgentApiIntentSource.cs'
$entry    = Join-Path $agentSrc 'Agent.Modules\Response\Intent\Composition\IntentExecutionComposition.cs'

$violations = $ctx.Violations

# 键集比较单源（A8）：唯一比较器 = C# LedgerKeyClosure(Ordinal)，经 LedgerForge compare-faces。
$ledgerForge = Get-GateLedgerForge -RepoRoot $Root
if (-not $ledgerForge) {
    Write-Host '[FAIL] 无法执行：LedgerForge 未构建（键集比较不可用）' -ForegroundColor Red
    exit 2
}

foreach ($file in @($registry, $backend, $entry)) {
    if (-not (Test-Path -LiteralPath $file)) { $violations.Add("缺少 IEF 面文件: $file") }
}

if ($violations.Count -eq 0) {
    # [P1] 写读同源声明（history.link）
    $regText = Get-Content -LiteralPath $registry -Raw
    if ($regText -notmatch 'AgentIntents\.HistoryLink') {
        $violations.Add('[P1] 注册表缺少 history.link 行')
    }
    if ($regText -notmatch 'WriteSurfaces\.GraphHistoryOwnsTask') {
        $violations.Add('[P1] history.link 未声明写面 graph:history_owns_task（读面只认图边）')
    }
    if ($regText -notmatch 'Read\(WriteSurfaces\.GraphHistoryOwnsTask\)') {
        $violations.Add('[P1] history.link 的读面未绑定唯一写面 graph:history_owns_task')
    }
    if ($regText -notmatch 'HistoryLink_WritesGraphEdge') {
        $violations.Add('[P1] history.link 缺对拍契约用例 ID（HistoryLink_WritesGraphEdge）')
    }

    # [P2] 后端必须调用注入写原语；禁止 no-op 成功
    $backendText = Get-Content -LiteralPath $backend -Raw
    if ($backendText -notmatch '_attachHistoryTask\s*\(') {
        $violations.Add('[P2] AgentApiIntentSource 未调用注入写原语 _attachHistoryTask（会退回改载荷冒充归拢）')
    }
    if ($backendText -match 'if\s*\(\s*item\s+is\s+null\s*\)\s*return\s+true') {
        $violations.Add('[P2] AgentApiIntentSource 仍存在「task 未建 → return true」no-op 成功')
    }

    # [P3] 组合入口经注册表探针 + Core 单一 handler 实现
    $entryText = Get-Content -LiteralPath $entry -Raw
    if ($entryText -notmatch 'IntentExecutionRegistry\.CreateDefault') {
        $violations.Add('[P3] 组合入口未使用执行注册表探针（能力位失真的回退口）')
    }
    if ($entryText -notmatch 'AgentIntentHandlerAssembly\.Build') {
        $violations.Add('[P3] 组合入口未复用 Agent.Core handler 单一实现')
    }
    if ($entryText -notmatch 'backend\.Supports') {
        $violations.Add('[P3] 组合入口未把域后端能力位（backend.Supports）注入注册表（探针双源风险）')
    }

    # [P4] 正对照：表非空
    if ($regText -notmatch 'AgentIntents\.TaskDelete') {
        $violations.Add('[P4] 正对照失败：注册表未包含 task.delete（表为空则上述判据恒真）')
    }

    # [P5] 覆盖面：注册表键集 == 已实现 handler 集（handler 的 Intent => AgentIntents.X）
    $handlerDir = Join-Path $agentSrc 'Agent.Core\Entry\Handlers'
    $handlerIntents = @(Get-ChildItem -LiteralPath $handlerDir -Filter *.cs -File -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw } |
        Select-String -Pattern 'Intent\s*=>\s*AgentIntents\.([A-Za-z0-9_]+)' -AllMatches |
        ForEach-Object { $_.Matches } |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique)
    $registryIntents = @([regex]::Matches($regText, 'yield return Row\(AgentIntents\.([A-Za-z0-9_]+)') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique)

    if ($handlerIntents.Count -eq 0 -or $registryIntents.Count -eq 0) {
        $violations.Add('[P5] 覆盖面判据无标的：handler/注册表 intent 集为空（判据恒真风险）')
    }
    else {
        # 键集比较统一到 C# LedgerKeyClosure(Ordinal)（A8）：本门不再自带 Compare-Object（大小写不敏感会假绿）。
        $diff = Compare-GateKeySet -Reference $handlerIntents -Other $registryIntents -ReferenceLabel 'handler' -OtherLabel 'registry' -RepoRoot $Root
        $missingRows = @($diff.OnlyLeft)
        $extraRows = @($diff.OnlyRight)
        if ($missingRows.Count -gt 0) {
            $violations.Add("[P5] 已实现 handler 缺执行绑定: $([string]::Join(',', $missingRows))")
        }
        if ($extraRows.Count -gt 0) {
            $violations.Add("[P5] 注册表存在无 handler 的 intent: $([string]::Join(',', $extraRows))")
        }
    }

    # [P6] 负对照：未声明的合成 intent 必须不在两侧集合（防白名单/注册表过宽）
    $synthetic = 'not.a.command'
    if ($handlerIntents -contains $synthetic -or $registryIntents -contains $synthetic) {
        $violations.Add('[P6] 负对照失败：未声明的合成 intent 被注册表/处理器承认')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'IntentParity'
