#requires -Version 5.1
<#
.SYNOPSIS
    提示词段装配守卫（PromptSegments）—— 执行体入口必须经段装配，短常量不得充当 L0。
.DESCRIPTION
    P1 会话管线不得以通用助手短句作 system 默认（等价短常量顶替 L0）。
    P2 会话种子 System 写点必须存在，且与 PromptCatalog 段集同源（band 可见化）。
    P3 会话链执行体步必须声明 system inport（结构可达）。
    P4 正/负对照：合成「短常量默认」样本必须被同一判据命中。
    默认报告；-Check 违规退出 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name PromptSegments
$minChecks = [int]$baseline.MinChecks.Value

$violations = [System.Collections.Generic.List[string]]::new()
$checks = 0

$pipelinePath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Orchestration\SessionWorkflow\ConversationPipeline.cs'
$seedPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Orchestration\SessionWorkflow\Circuit\ConversationStageBodies.cs'
$contractPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Contract\AgentSystemPrompts.cs'
$workflowsDir = Join-Path $repoRoot 'Projects\Plug\Agent\Config\workflows'

function Test-ShortConstantDefault {
    param([AllowEmptyString()][string]$Text)
    return [regex]::IsMatch($Text, '\?\s*AgentSystemPrompts\.ConversationAssistant\b')
}

# ---------------- P1 管线默认来源 ----------------
if (-not (Test-Path -LiteralPath $pipelinePath)) {
    $violations.Add("P1 会话管线缺失：$pipelinePath")
} else {
    $pipeline = Get-Content -LiteralPath $pipelinePath -Raw
    if (Test-ShortConstantDefault -Text $pipeline) {
        $violations.Add('P1 会话管线仍以 AgentSystemPrompts.ConversationAssistant 作 system 默认（短常量顶替 L0）')
    }
    if ($pipeline -notmatch 'PromptCatalog\.L0SystemPromptFor') {
        $violations.Add('P1 会话管线未引用 PromptCatalog.L0SystemPromptFor（段装配未接入）')
    }
    $checks++
}

# ---------------- P2 种子写点与段来源 ----------------
if (-not (Test-Path -LiteralPath $seedPath)) {
    $violations.Add("P2 会话种子缺失：$seedPath")
} else {
    $seed = Get-Content -LiteralPath $seedPath -Raw
    if ($seed -notmatch 'RoundKeys\.System\]\s*=') {
        $violations.Add('P2 会话种子未写 RoundKeys.System（执行体入口无 system 来源）')
    }
    if ($seed -notmatch 'L0ProtocolFor') {
        $violations.Add('P2 会话种子未与 L0 段集同源（band 不可见）')
    }
    $checks++
}

# ---------------- P3 会话链执行体步 inports ----------------
foreach ($file in @('session-cline.ushar', 'session-answer.ushar')) {
    $path = Join-Path $workflowsDir $file
    if (-not (Test-Path -LiteralPath $path)) { $violations.Add("P3 会话链缺失：$file"); continue }
    $text = Get-Content -LiteralPath $path -Raw
    $nodes = [regex]::Matches($text, '(?m)^\[Node\].*kind=external.*$')
    if ($nodes.Count -eq 0) { $violations.Add("P3 $file 无 external 执行体步"); continue }
    foreach ($n in $nodes) {
        if ($n.Value -notmatch 'inports=[^\r\n]*\bsystem\b') {
            $violations.Add("P3 $file 执行体步缺 system inport（段装配不可达）")
        }
    }
    $checks++
}

# ---------------- P4 负对照 ----------------
if (-not (Test-ShortConstantDefault -Text 'string.IsNullOrWhiteSpace(x) ? AgentSystemPrompts.ConversationAssistant : x')) {
    $violations.Add('P4 负对照失效：合成短常量默认未被判据命中')
}
$checks++

if ($checks -lt $minChecks) {
    $violations.Add("正对照失效：实际判据数 $checks < 基线 $minChecks（判据面漂移）")
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] PromptSegments：0 违规（判据数 $checks）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] PromptSegments 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
