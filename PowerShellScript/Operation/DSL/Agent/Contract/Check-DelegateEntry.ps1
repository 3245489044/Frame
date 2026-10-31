#requires -Version 5.1
<#
.SYNOPSIS
    委派入口守卫（DelegateEntry）—— 外部一次调用拿到子智能体结果的入口须「可发现 / 已装配 / 进模型面 / 真等待」。

.DESCRIPTION
    判据（D1–D5）：
      D1 声明面：Config/tool-face.ushar 的 [Space] ToolTrack 含 delegate_subagent，且 registry/loopback
                 各有一条 [Ref] 指向该轨（面→轨事实），ToolName 空间暴露给 model。
      D6 cline 面：ToolFace:cline 存在且只挂 delegate_subagent（禁挂 ps_runtime）。
      D2 装配面：ToolFaceAssembler.TrackFactories 恰 1 处 `["delegate_subagent"] =`，且构造 DelegateSubAgentCallableTrack。
      D3 模型面：ToolCatalogCore 的 Names 含 delegate_subagent（外部 AI 可发现）。
      D4 执行面：DelegateSubAgentCallableTrack 引用 ITaskAwaitPort（同步等待端口，禁 fire-and-forget 假委派）。
      D5 正/负对照：D1 的判定函数对真实声明为真，对剥离委派行的合成文本为假（防恒真）。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。
    依赖方向（硬约束）：本件只读仓库；零侧写。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name DelegateEntry
$minChecks = [int]$baseline.MinChecks.Value

$violations = [System.Collections.Generic.List[string]]::new()
$checks = 0

$facePath = Join-Path $repoRoot 'Projects\Plug\Agent\Config\tool-face.ushar'
$assemblerPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.McpRegistryHost\Faces\ToolFaceAssembler.cs'
$trackPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.McpRegistryHost\Tracks\DelegateSubAgentCallableTrack.cs'
$catalogDir = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Operation\Tool'

function Test-DelegateTrackDeclared {
    param([AllowEmptyString()][string]$Text)
    return ($Text -match '(?m)^\s*delegate_subagent\s*$')
}

# ---------------- D1 声明面 ----------------
if (-not (Test-Path -LiteralPath $facePath)) {
    $violations.Add("D1 声明面缺失：$facePath")
} else {
    $face = Get-Content -LiteralPath $facePath -Raw
    if (-not (Test-DelegateTrackDeclared -Text $face)) {
        $violations.Add("D1 tool-face.ushar 未声明轨 delegate_subagent（[Space] ToolTrack）")
    }
    if ($face -notmatch '(?m)^\[Ref\]\s+\w*DelegateSubAgent\b.*src=delegate_subagent') {
        $violations.Add("D1 tool-face.ushar 缺面→轨事实（[Ref] <x>DelegateSubAgent … src=delegate_subagent）")
    }
    if ($face -notmatch '(?m)^\[Ref\]\s+ExposedDelegateSubAgent\b.*tgt=model') {
        $violations.Add("D1 tool-face.ushar 未把 delegate_subagent 暴露到模型面（[Ref] ExposedDelegateSubAgent … tgt=model）")
    }
    $checks++

    # D6 cline 面（P1）：只挂委派轨，禁挂 PS 执行轨（cline 面声明 ↔ SDK 工具注入清单）。
    if ($face -notmatch '(?m)^\[Ref\].*\bToolFace:cline\b.*edge=hosts.*tgt=delegate_subagent') {
        $violations.Add('D6 tool-face.ushar 缺 cline 面 → delegate_subagent 的 hosts 边（cline 内委派不可注入）')
    }
    if ($face -match '(?m)^\[Ref\].*\bToolFace:cline\b.*tgt=ps_runtime') {
        $violations.Add('D6 cline 面不得挂 ps_runtime（禁 PS 直执行）')
    }
    $checks++
    $stripped = ($face -replace '(?m)^\s*delegate_subagent\s*\r?\n?', '')
    if (Test-DelegateTrackDeclared -Text $stripped) {
        $violations.Add('D5 负对照失效：剥离 delegate_subagent 行后判据仍为真（判据恒真）')
    }
    if (-not (Test-DelegateTrackDeclared -Text "delegate_subagent")) {
        $violations.Add('D5 正对照失效：最小样本未被判据命中')
    }
    $checks++
}

# ---------------- D2 装配面 ----------------
if (-not (Test-Path -LiteralPath $assemblerPath)) {
    $violations.Add("D2 装配面缺失：$assemblerPath")
} else {
    $assembler = Get-Content -LiteralPath $assemblerPath -Raw
    $hits = [regex]::Matches($assembler, '\[\s*"delegate_subagent"\s*\]\s*=')
    if ($hits.Count -ne 1) {
        $violations.Add("D2 TrackFactories 注册 delegate_subagent 次数=$($hits.Count)（应恰为 1）")
    }
    if ($assembler -notmatch 'delegate_subagent"\s*\]\s*=\s*static[^\r\n]*DelegateSubAgentCallableTrack') {
        $violations.Add('D2 delegate_subagent 轨工厂未构造 DelegateSubAgentCallableTrack')
    }
    $checks++
}

# ---------------- D3 模型面 ----------------
$catalogHit = $false
if (Test-Path -LiteralPath $catalogDir) {
    foreach ($file in (Get-ChildItem -LiteralPath $catalogDir -Filter 'ToolCatalogCore*.cs' -File)) {
        if ((Get-Content -LiteralPath $file.FullName -Raw) -match '"delegate_subagent"') { $catalogHit = $true }
    }
}
if (-not $catalogHit) {
    $violations.Add('D3 ToolCatalogCore.Names 未登记 delegate_subagent（外部 AI 不可发现）')
}
$checks++

# ---------------- D4 执行面 ----------------
if (-not (Test-Path -LiteralPath $trackPath)) {
    $violations.Add("D4 执行面缺失：$trackPath")
} else {
    $track = Get-Content -LiteralPath $trackPath -Raw
    if ($track -notmatch 'ITaskAwaitPort') {
        $violations.Add('D4 DelegateSubAgentCallableTrack 未引用 ITaskAwaitPort（无同步等待即伪委派）')
    }
    $checks++
}

# ---------------- 正对照：判据面非空 ----------------
if ($checks -lt $minChecks) {
    $violations.Add("正对照失效：实际判据数 $checks < 基线 $minChecks（判据面漂移）")
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host "[OK] DelegateEntry：0 违规（声明/装配/模型面/等待端口 均单源在位；判据数 $checks）" -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] DelegateEntry 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：委派入口 = tool-face.ushar 声明 + TrackFactories 一行 + ToolCatalogCore 一项 + ITaskAwaitPort 等待。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0
