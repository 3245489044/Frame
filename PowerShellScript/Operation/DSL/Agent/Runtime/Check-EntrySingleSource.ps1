#requires -Version 5.1
<#
.SYNOPSIS
    入口单源守卫（EntrySingleSource）—— 「写库即实现」单一入口核心的建面/对账判据。

.DESCRIPTION
    沿《统一入口迁移至 Agent.Core-写库即实现-实施蓝图》§7：
    入口核心（Agent.Core.Entry）必须是唯一 json→json 门 / 唯一调度 / 唯一映射 / 唯一 MCP 通用轨。

    判据（E-1/E-1b/E-2/E-3 + 对照）：
      E-1 入口门单源：`class AgentApiPort` 定义恰 1 处
      E-1b 通用轨单源：`class AgentEntryCallableTrack` 定义恰 1 处
      E-2 映射单源：`class AgentIntentMapping` 定义恰 1 处
      E-3 分派单源：`class AgentDispatcher` 定义恰 1 处
      E-4 转发面：`AgentEntryCallableTrack.CallAsync` 经 `AgentEntryPort` 转发（命中 ≥ 1）
      P-1 正对照：注册表 intent 登记数 ≥ 基线（登记被删而门禁恒绿 = 失效）
      P-2 负对照：合成「两个 class AgentApiPort 定义」须被判据识别
    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规 → 退出码 1）。
.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯推导）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
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
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'EntrySingleSource'
$Root = $ctx.Root
$scanRoots = @(
    (Join-Path $Root 'Projects\Plug\Agent\src'),
    (Join-Path $Root 'Frame\CsharpPrj')
)
$exclude = '\\BuildOutput\\|\\obj\\|\\docs\\'

function Get-SourceFiles {
    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $scanRoots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        foreach ($f in @(Get-ChildItem -LiteralPath $r -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
            if ($f.FullName -match $exclude) { continue }
            $list.Add($f.FullName)
        }
    }
    return $list
}

function Get-DefinitionCount {
    param([string[]]$Files, [string]$Pattern)
    $count = 0
    foreach ($f in $Files) {
        $count += @(Select-String -LiteralPath $f -Pattern $Pattern -ErrorAction SilentlyContinue).Count
    }
    return $count
}

$violations = $ctx.Violations
$files = Get-SourceFiles

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  入口单源守卫（AgentApiPort / AgentDispatcher / AgentIntentMapping / AgentEntryCallableTrack）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---- E-1 / E-1b / E-2 / E-3 定义单源 ----
$portDefs = Get-DefinitionCount -Files $files -Pattern 'class\s+AgentApiPort\b'
$trackDefs = Get-DefinitionCount -Files $files -Pattern 'class\s+AgentEntryCallableTrack\b'
$mapDefs = Get-DefinitionCount -Files $files -Pattern 'class\s+AgentIntentMapping\b'
$dispatchDefs = Get-DefinitionCount -Files $files -Pattern 'class\s+AgentDispatcher\b'

if ($portDefs -ne 1) { $violations.Add("[E-1] class AgentApiPort 定义 $portDefs 处（须恰 1）") }
if ($trackDefs -ne 1) { $violations.Add("[E-1b] class AgentEntryCallableTrack 定义 $trackDefs 处（须恰 1）") }
if ($mapDefs -ne 1) { $violations.Add("[E-2] class AgentIntentMapping 定义 $mapDefs 处（须恰 1）") }
if ($dispatchDefs -ne 1) { $violations.Add("[E-3] class AgentDispatcher 定义 $dispatchDefs 处（须恰 1）") }

# ---- E-4 通用轨经入口端口转发 ----
$forwardHits = Get-DefinitionCount -Files $files -Pattern 'AgentEntryPort\.Current'
if ($forwardHits -lt 1) { $violations.Add('[E-4] 未发现 AgentEntryPort.Current 转发点（MCP 通用轨未接入口）') }

# ---- P-1 正对照：注册表 intent 登记数 ≥ 基线 ----
$intentsFile = $files | Where-Object { $_ -match 'AgentIntents\.cs$' } | Select-Object -First 1
$intentCount = 0
if ($intentsFile) {
    $intentCount = @(Select-String -LiteralPath $intentsFile -Pattern 'public\s+const\s+string\s+' -ErrorAction SilentlyContinue).Count
}
$minIntents = 25
if ($intentCount -lt $minIntents) {
    $violations.Add("[P-1] 正对照基线：intent 常量数 $intentCount < 基线 $minIntents（登记被删而门禁恒绿 = 失效）")
}

# ---- P-2 负对照 ----
$probe = "class AgentApiPort {}`nclass AgentApiPort {}`nclass AgentDispatcher {}"
$probePortDefs = @([regex]::Matches($probe, 'class\s+AgentApiPort\b')).Count
if ($probePortDefs -le 1) {
    $violations.Add('[P-2] 负对照失效：合成双定义未被判据识别（判据恒真/恒假？）')
}

Write-Host ("  实测：ApiPort {0} / AgentDispatcher {1} / AgentIntentMapping {2} / AgentEntryCallableTrack {3} / 转发点 {4} / intent 常量 {5}" -f `
    $portDefs, $dispatchDefs, $mapDefs, $trackDefs, $forwardHits, $intentCount) -ForegroundColor Gray

Write-GateVerdict -Violations $violations -Check:$Check -Key 'EntrySingleSource'
