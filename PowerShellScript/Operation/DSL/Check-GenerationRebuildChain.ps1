# Check-GenerationRebuildChain.ps1
# VM-R5 · 换代链结构守卫 —— 校验「构建到新目录 → 换 ALC → 重装配 → 数据全在根」四段的**结构事实**是否在位
# （对齐 Docs\analysis\2026\10\10.9\2026-10-09-Vm框架-换代路线收敛-剩余步骤施工单.md §4.2–§4.4）。
#
# 判据（结构事实，非执行者叙述；逐项 = 文件存在 ∧ 关键符号在位）：
#   [C1] 代目录布局单源（ExtensionGenerationLayout.TryBuildGenerationManifests）
#   [C2] 重装配桥（AgentGenerationRebuildBridge 四步 + AgentGenerationPortRebind）
#   [C3] 按代端口注册（PrjBridgeRegistrar.RegisterPortsForGeneration）
#   [C4] 宿主接线入口（LauncherBootstrap.TryRebuildAgentAssemblyAsync）
#   [C5] F 段重挂 + 触发面（AgentVmProgramHost：MountSlot / StartSourceWatch / TryTriggerGenerationSwapAsync / RegisterGenerationSwapTrigger）
#   [C6] 源码监视模式（VmProgramHotReloadWatcher：sourceRoot）
#   [C7] 装配契约（AgentAssemblyContract.ColdStartEquivalent）
#   [C8] 反射单缝（PrjBridgeInvoker：RegisterPorts / RegisterPortsForGeneration 共用）
#   [C9] 换代唯一触发逻辑（AgentHotSwapPort.TriggerOrRebuildAsync：宿主门面与函数库单源）
# 说明：本脚本只读校验（不构建/不换代）；报告模式列事实，-Check 违规 > 0 → 退出码 1。
#
# 用法：pwsh Frame\PowerShellScript\Operation\DSL\Check-GenerationRebuildChain.ps1 [-Root <repo>] [-Check]

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

function Test-Anchor {
    param([string]$Id, [string]$RelPath, [string[]]$Patterns)
    $full = Join-Path $Root $RelPath
    if (-not (Test-Path -LiteralPath $full)) {
        $violations.Add(("[{0}] 缺件：{1}" -f $Id, $RelPath))
        return
    }
    $text = Get-Content -LiteralPath $full -Raw
    foreach ($p in $Patterns) {
        if ($text -notmatch $p) {
            $violations.Add(("[{0}] {1} 未命中符号：{2}" -f $Id, $RelPath, $p))
        }
    }
}

Test-Anchor 'C1' 'Frame\CsharpPrj\Modules\Registry\ExtensionGenerationLayout.cs' @('class ExtensionGenerationLayout', 'TryBuildGenerationManifests')
Test-Anchor 'C2' 'Projects\FileDriven\FileDriven.Execution\Lifecycle\Generation\AgentGenerationRebuildBridge.cs' @('class AgentGenerationRebuildBridge', 'class AgentGenerationPortRebind', '_registerPorts', '_ensureReady')
Test-Anchor 'C3' 'Projects\PrjBridge\Composition\PrjBridgeRegistrar.cs' @('RegisterPortsForGeneration')
Test-Anchor 'C4' 'Projects\FileDriven\FileDriven.Launcher\Bootstrap\LauncherBootstrap.cs' @('TryRebuildAgentAssemblyAsync')
Test-Anchor 'C5' 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Circuit\AgentVmProgramHost.cs' @('MountSlot', 'StartSourceWatch', 'TryTriggerGenerationSwapAsync', 'RegisterGenerationSwapTrigger')
Test-Anchor 'C6' 'Projects\Vm\Core\VmProgramHotReloadWatcher.cs' @('sourceRoot')
Test-Anchor 'C7' 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Root\AgentAssemblyContract.cs' @('ColdStartEquivalent')
Test-Anchor 'C8' 'Projects\FileDriven\FileDriven.Execution\Lifecycle\PrjBridgeInvoker.cs' @('class PrjBridgeInvoker', 'TryInvoke')
Test-Anchor 'C9' 'Projects\FileDriven\FileDriven.Abstractions\Ports\Agent\AgentHotSwapPort.cs' @('TriggerOrRebuildAsync')

Write-Host '=== [VM-R5] 换代链结构守卫（构建→换 ALC→重装配→数据全在根） ==='
Write-Host (("锚点检查: C1–C9 | 违规命中: {0}" -f $violations.Count))
if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---'
    $violations | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
}

if ($Check -and $violations.Count -gt 0) {
    Write-Host '校验失败：换代链结构锚点缺失（链未接通或符号改名未同步）。'
    exit 1
}
exit 0
