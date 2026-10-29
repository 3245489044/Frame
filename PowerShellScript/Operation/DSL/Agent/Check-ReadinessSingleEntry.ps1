#requires -Version 5.1
<#
.SYNOPSIS
    就绪/诊断单点守卫（ReadinessSingleEntry）—— F4 就绪/诊断单点框架。

.DESCRIPTION
    判据：
      G1 就绪单点：`ReadinessFace.Of(` 定义 = 1；旧 `RenderReadinessHtml` 定义/引用 = 0。
      G2 诊断声明驱动：恒假常量 `const bool IncludeGatherDiagnostics = false` = 0；`GatherDiagnosticsSwitch` 在位。
      G3 正/负对照：合成样本（旧 `RenderReadinessHtml(` 调用）必被 G1 捕获。

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

Write-Host ''
Write-Host '============================================================'
Write-Host '  就绪/诊断单点守卫（ReadinessSingleEntry：F4）'
Write-Host '============================================================'

function Get-Count([string]$Text, [string]$Pattern) { return [regex]::Matches($Text, $Pattern).Count }

$uiDir       = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui'
$faceFile    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\ReadinessFace.cs'
$viewFile    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Orchestration\ConversationViewService.cs'

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
foreach ($f in (Get-ChildItem -LiteralPath $uiDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
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

# 正/负对照
if ((Get-Count 'return RenderReadinessHtml("capability:agent.gather");' 'RenderReadinessHtml\s*\(') -eq 0) {
    $violations.Add('[G3] 负对照失效：旧就绪调用合成样本未被判据捕获')
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2 就绪单点 + 诊断声明驱动通过' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
