#requires -Version 5.1
<#
.SYNOPSIS
    就绪/诊断单点守卫（ReadinessSingleEntry）—— F4 就绪/诊断单点框架。

.DESCRIPTION
    判据：
      G1 就绪单点：`ReadinessFace.Of(` 定义 = 1；旧 `RenderReadinessHtml` 定义/引用 = 0。
      G2 诊断声明驱动：恒假常量 `const bool IncludeGatherDiagnostics = false` = 0；`GatherDiagnosticsSwitch` 在位。
      G3 正/负对照：合成样本（旧 `RenderReadinessHtml(` 调用）必被 G1 捕获。
    正/负对照由 Assert-GateControls 统一执行。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateFileIndex.psm1') -Force
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'ReadinessSingleEntry'
$violations = $ctx.Violations

Write-GateHeader '就绪/诊断单点守卫（ReadinessSingleEntry：F4）'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

# 判据自证
$controls = @(
    @{ Id = 'G3'; Predicate = { param($t) $t -match 'RenderReadinessHtml\s*\(' }
       Good = 'return ReadinessFace.Of("capability:agent.gather");'; Bad = 'return RenderReadinessHtml("capability:agent.gather");' }
    @{ Id = 'G2'; Predicate = { param($t) $t -match 'const\s+bool\s+IncludeGatherDiagnostics\s*=\s*false' }
       Good = 'var enabled = GatherDiagnosticsSwitch.Value;'; Bad = 'const bool IncludeGatherDiagnostics = false;' }
)
foreach ($issue in (Assert-GateControls -Cases $controls -Label 'ReadinessSingleEntry')) { $violations.Add($issue) }

$agentSrc    = Get-MappedPath -Key 'AgentSrc'
$uiDir       = Join-Path $agentSrc 'Agent.Ui'
$faceFile    = Join-Path $agentSrc 'Agent.Ui\Ui\Views\Declaration\ReadinessFace.cs'
$viewFile    = Join-Path $agentSrc 'Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'

if (-not (Test-Path -LiteralPath $faceFile)) {
    $violations.Add("[G1] 缺件（判据不可判）：$faceFile")
} else {
    $ft = [System.IO.File]::ReadAllText($faceFile)
    if ((Get-Count $ft 'internal\s+static\s+string\s+Of\s*\(') -ne 1) {
        $violations.Add('[G1] ReadinessFace.Of 定义 ≠ 1（就绪单点缺失）')
    }
    if ($ft -notmatch 'GatherDiagnosticsSwitch') {
        $violations.Add('[G2] GatherDiagnosticsSwitch 未在位（诊断开关未声明驱动）')
    }
}

$oldDefs = 0; $oldRefs = 0
$files = @(Get-GateFiles -Root $uiDir -Extension cs -Exclude '\\(bin|obj|BuildOutput)\\')
foreach ($f in $files) {
    $t = [System.IO.File]::ReadAllText($f)
    $oldDefs += (Get-Count $t 'private\s+static\s+string\s+RenderReadinessHtml\s*\(')
    $oldRefs += (Get-Count $t 'RenderReadinessHtml\s*\(')
}
if ($oldDefs -ne 0) { $violations.Add("[G1] 旧就绪实现 RenderReadinessHtml 定义 = $oldDefs（应 0）") }
if ($oldRefs -ne 0) { $violations.Add("[G1] 旧 RenderReadinessHtml 引用 = $oldRefs（应 0；统一经 ReadinessFace.Of）") }

if (Test-Path -LiteralPath $viewFile) {
    $vt = [System.IO.File]::ReadAllText($viewFile)
    if ($vt -match 'const\s+bool\s+IncludeGatherDiagnostics\s*=\s*false') {
        $violations.Add('[G2] 残留恒假常量 IncludeGatherDiagnostics = false（应声明驱动）')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ReadinessSingleEntry'
