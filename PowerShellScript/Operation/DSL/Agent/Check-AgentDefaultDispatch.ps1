# 默认路由门禁（P5-A/B）：结构化意图 → 标签 → 执行腿成对，声明面齐备。
# R-07 迁移：实现单一来源 = 本文件（Frame\PowerShellScript\Operation\DSL\Agent），由 GateManifests.AgentDefaultDispatch 聚合。
[CmdletBinding()]
param([string]$AgentRoot = '')

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($AgentRoot)) {
    # 迁址后根定位：从脚本位置上溯到仓库根（同时含 Projects 与 Frame），再定位 Agent 工程根。
    $repoRoot = $PSScriptRoot
    while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
        if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
        $repoRoot = Split-Path $repoRoot -Parent
    }
    $AgentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
}

$violations = New-Object System.Collections.Generic.List[string]
$subagents = Get-Content -LiteralPath (Join-Path $AgentRoot 'Config\subagents.ushar') -Raw -Encoding UTF8
$modes = Get-Content -LiteralPath (Join-Path $AgentRoot 'Config\history-modes.ushar') -Raw -Encoding UTF8
$loop = Get-Content -LiteralPath (Join-Path $AgentRoot 'Config\history-loop.ushar') -Raw -Encoding UTF8

$pairs = @(
    @{ Tag = 'tool-call'; Executor = 'subagent-tool-call' },
    @{ Tag = 'workflow'; Executor = 'subagent-workflow' },
    @{ Tag = 'base'; Executor = 'subagent-base' },
    @{ Tag = 'powershell'; Executor = 'subagent-ps' }
)
foreach ($p in $pairs) {
    if ($subagents -notmatch ("\[SubAgent\]\s+" + [regex]::Escape($p.Tag) + ":")) {
        $violations.Add("缺标签 $($p.Tag)")
    }
    if ($subagents -notmatch ("executor\s*=\s*" + [regex]::Escape($p.Executor))) {
        $violations.Add("缺执行腿 $($p.Executor)")
    }
}
# powershell 区块（E18）：按 [SubAgent] 区块解析，禁止全文匹配被其他子智能体掩盖。
$psBlock = [regex]::Match($subagents, '(?ms)^\[SubAgent\]\s+powershell:\s*(.*?)(?=^\[SubAgent\]|\z)')
if (-not $psBlock.Success) {
    $violations.Add('缺 [SubAgent] powershell 区块')
} else {
    if ($psBlock.Groups[1].Value -notmatch 'executor\s*=\s*subagent-ps') {
        $violations.Add('powershell 区块缺执行腿 subagent-ps')
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
    # 行为判据（E18）：file 意图必须经声明键读取，禁止在分类器内硬编码标签/执行腿。
    if ($text -notmatch 'DefaultFileSubAgentKey') {
        $violations.Add('分类器未使用声明键 DefaultFileSubAgentKey（file 标签必须声明驱动）')
    }
    if ($text -notmatch 'DefaultFileExecutorKey') {
        $violations.Add('分类器未使用声明键 DefaultFileExecutorKey（file 执行腿必须声明驱动）')
    }
}
if ($modes -notmatch 'tool\.call') { $violations.Add('history-modes.ushar 缺 tool.call') }
foreach ($key in @('default_tool_sub_agent','default_workflow_sub_agent','default_base_sub_agent','default_file_sub_agent','default_file_executor','structured_submission_creates_member')) {
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
