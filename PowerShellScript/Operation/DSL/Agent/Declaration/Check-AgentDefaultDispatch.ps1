# 默认路由门禁（P5-A/B）：结构化意图 → 标签 → 执行腿成对，声明面齐备。
# R-07 迁移：实现单一来源 = 本文件（Frame\PowerShellScript\Operation\DSL\Agent），由 GateManifests.AgentDefaultDispatch 聚合。
[CmdletBinding()]
param([string]$AgentRoot = '')

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($AgentRoot)) {
    # 迁址后根定位：从脚本位置上溯到仓库根（同时含 Projects 与 Frame），再定位 Agent 工程根。
    $repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    $AgentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
}

$violations = New-Object System.Collections.Generic.List[string]
$subagents = Get-Content -LiteralPath (Join-Path $AgentRoot 'Config\subagents.ushar') -Raw -Encoding UTF8
$modes = Get-Content -LiteralPath (Join-Path $AgentRoot 'Config\history-modes.ushar') -Raw -Encoding UTF8
$loop = Get-Content -LiteralPath (Join-Path $AgentRoot 'Config\history-loop.ushar') -Raw -Encoding UTF8

$pairs = @(
    @{ Tag = 'tool-call'; Kind = 'tool-invoke' },
    @{ Tag = 'workflow'; Kind = 'workflow' },
    @{ Tag = 'base'; Kind = 'state-response' },
    @{ Tag = 'powershell'; Kind = 'llm' },
    @{ Tag = 'usharp'; Kind = 'llm' },
    @{ Tag = 'tool-catalog'; Kind = 'tool-catalog' }
)
foreach ($p in $pairs) {
    $block = [regex]::Match($subagents, ('(?ms)^\[SubAgent\]\s+' + [regex]::Escape($p.Tag) + ':\s*(.*?)(?=^\[SubAgent\]|\z)'))
    if (-not $block.Success) {
        $violations.Add("缺标签 $($p.Tag)")
        continue
    }
    $body = $block.Groups[1].Value
    if ($body -notmatch 'step_mode\s*=\s*single') {
        $violations.Add("$($p.Tag) 区块缺 step_mode=single（单步框架唯一入口）")
    }
    if ($body -notmatch ('step_kind\s*=\s*' + [regex]::Escape($p.Kind))) {
        $violations.Add("$($p.Tag) 区块缺 step_kind=$($p.Kind)")
    }
    $declaredLeg = [regex]::Match($body, 'executor\s*=\s*(?<leg>\S+)')
    if ($declaredLeg.Success -and $declaredLeg.Groups['leg'].Value -ne 'subagent-single-step') {
        $violations.Add("$($p.Tag) 执行腿漂移：$($declaredLeg.Groups['leg'].Value)（唯一单步腿 = subagent-single-step）")
    }
}
# powershell 区块（E18）：按 [SubAgent] 区块解析，禁止全文匹配被其他子智能体掩盖。
$psBlock = [regex]::Match($subagents, '(?ms)^\[SubAgent\]\s+powershell:\s*(.*?)(?=^\[SubAgent\]|\z)')
if (-not $psBlock.Success) {
    $violations.Add('缺 [SubAgent] powershell 区块')
} else {
    if ($psBlock.Groups[1].Value -notmatch 'step_next\s*=\s*tool-invoke') {
        $violations.Add('powershell 区块缺 step_next=tool-invoke（工具意图交 tool-invoke）')
    }
    if ($psBlock.Groups[1].Value -notmatch 'tool_face\s*=\s*full') {
        $violations.Add('powershell 区块缺 tool_face=full（执行面全量）')
    }
}

$classifier = Join-Path $AgentRoot 'src\Agent.Modules\Response\Runtime\HistorySubmissionClassifier.cs'
if (-not (Test-Path -LiteralPath $classifier)) {
    $violations.Add('缺结构化意图分类器 HistorySubmissionClassifier.cs')
} else {
    $text = Get-Content -LiteralPath $classifier -Raw -Encoding UTF8
    # 行为判据（E18/D6）：file 标签经声明键读取；执行腿经目录解析（唯一来源），禁分类器内硬编码。
    if ($text -notmatch 'DefaultFileSubAgentKey') {
        $violations.Add('分类器未使用声明键 DefaultFileSubAgentKey（file 标签必须声明驱动）')
    }
    if ($text -notmatch 'ISubAgentCatalog') {
        $violations.Add('分类器未使用 ISubAgentCatalog（执行腿须按声明解析）')
    }
    if ($text -notmatch 'SubAgentExecutorIds\.SingleStep') {
        $violations.Add('分类器缺唯一单步腿回退（SubAgentExecutorIds.SingleStep）')
    }
    if ($text -match 'subagent-(tool-call|workflow|base|ps|usharp)') {
        $violations.Add('分类器残留旧执行腿硬编码（禁 per-intent 映射）')
    }
}
if ($modes -notmatch 'tool\.call') { $violations.Add('history-modes.ushar 缺 tool.call') }
foreach ($key in @('default_tool_sub_agent','default_workflow_sub_agent','default_base_sub_agent','default_file_sub_agent','default_file_executor','structured_submission_creates_member','include_new_members','include_member_nodes')) {
    if ($loop -notmatch [regex]::Escape($key)) { $violations.Add("history-loop.ushar 缺 $key") }
}

# R-01 Data 声明镜像：运行时优先读 Data 根 ⇒ HistoryDispatchPolicy 键集与 [Ref] src/tgt 集须与仓库等价。
# Data 缺失 = 干净 checkout，跳过并提示（不伪造一致）。
function Get-PolicyKeys {
    param([string]$Text)
    $m = [regex]::Match($Text, '(?ms)^\[Space\]\s+HistoryDispatchPolicy:\s*(.*?)(?=^\[|\z)')
    if (-not $m.Success) { return @() }
    return @([regex]::Matches($m.Groups[1].Value, '(?m)^\s*(?<k>[a-z_]+)\s*$') |
        ForEach-Object { $_.Groups['k'].Value } | Sort-Object -Unique)
}
function Get-RefPairs {
    param([string]$Text)
    return @([regex]::Matches($Text, '(?m)^\[Ref\][^\r\n]*?src=(?<src>[A-Za-z0-9_]+)[^\r\n]*?tgt=(?<tgt>[A-Za-z0-9_-]+)') |
        ForEach-Object { $_.Groups['src'].Value + '>' + $_.Groups['tgt'].Value } | Sort-Object -Unique)
}

$mainRoot = Split-Path (Split-Path (Split-Path $AgentRoot -Parent) -Parent) -Parent
$dataLoopPath = Join-Path $mainRoot 'Data\Agent\Config\history-loop.ushar'
if (Test-Path -LiteralPath $dataLoopPath) {
    $dataLoop = Get-Content -LiteralPath $dataLoopPath -Raw -Encoding UTF8
    $repoKeys = Get-PolicyKeys -Text $loop
    $dataKeys = Get-PolicyKeys -Text $dataLoop
    foreach ($k in $repoKeys) {
        if ($dataKeys -notcontains $k) { $violations.Add("Data 镜像缺声明键 $k（R-01：运行时读 Data，声明不生效）") }
    }
    $repoRefs = Get-RefPairs -Text $loop
    $dataRefs = Get-RefPairs -Text $dataLoop
    foreach ($r in $repoRefs) {
        if ($dataRefs -notcontains $r) { $violations.Add("Data 镜像缺 [Ref] $r（R-01）") }
    }
}
else {
    Write-Host "  ! Data 根无 history-loop.ushar（干净 checkout，跳过镜像对账）" -ForegroundColor Yellow
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'AgentDefaultDispatch gate OK' -ForegroundColor Green
exit 0
