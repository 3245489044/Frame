#requires -Version 5.1
<#
.SYNOPSIS
    UI 单入口门禁（UiSingleEntry）—— UI 只经 IAgentUiConnector，不得直取后端静态槽/legacy 路由。

.DESCRIPTION
    判据载体 = 源码静态计数 + 生成目录解析（结构事实；非执行者叙述）。权威 = 连接点重构方案 R7：
      C0 登记在位：GateManifests.psd1 含 UiSingleEntry 登记块
      C1 Core/Functions/** 与 Ui/Views/** 的 AgentUiHostAccess.Access 命中数 ≤ 基线（棘轮只降不升）
      C2 同上 AgentServiceSlots.Resolve 命中数 ≤ 基线
      C3 同上 AgentUiHost. 命中数 ≤ 基线
      C4 目录 handler=LegacyRoute 行数 = 0（R1 完成后启用）
      C5 目录每个 op 均声明 handler，且 handler 在领域 handler 词表内（闭合校验）
      C6 负对照：合成样本必须被同一个检测器判红（防假绿）
      C7 正对照：基线计数 > 0 且检测器当前有命中（判据不得无标的）

    模式：默认报告；-Check 违规 → 退出码 1；-WriteBaseline 仅写 Baselines\Process\UiSingleEntry.psd1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$WriteBaseline,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$baselineRel = 'Frame\PowerShellScript\Baselines\Process\UiSingleEntry.psd1'
$baselinePath = Join-Path $Root $baselineRel
$registrationRel = 'Frame\PowerShellScript\Operation\DSL\Registry\GateManifests.psd1'
$catalogRel = 'Projects\Plug\Agent\Config\commands\ui-commands.generated.json'

$patterns = [ordered]@{
    Access = 'AgentUiHostAccess\.Access'
    Slots  = 'AgentServiceSlots\.Resolve'
    Host   = 'AgentUiHost\.'
}
$scanRoots = @(
    'Projects\Plug\Agent\src\Agent.Ui\Core\Functions',
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views'
)
$handlerVocabulary = @(
    'ConversationHandler', 'TaskHandler', 'NodeHandler',
    'HistoryHandler', 'FlagHandler', 'MiscHandler'
)

function Get-Counts {
    param([string]$Base)
    $result = @{ Access = 0; Slots = 0; Host = 0 }
    foreach ($relRoot in $scanRoots) {
        $dir = Join-Path $Base $relRoot
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $files = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs'
        foreach ($file in $files) {
            $text = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($name in $patterns.Keys) {
                $result[$name] += ([regex]::Matches($text, $patterns[$name])).Count
            }
        }
    }
    return $result
}

function New-Baseline {
    param([hashtable]$Counts)
    return @"
@{
    Schema    = 'ui-single-entry-v1'
    Carrier   = '源码静态计数（Core/Functions + Ui/Views 的静态槽直取点）+ 生成目录 handler 解析'
    SingleEntry = @{
        Access = $([int]$Counts.Access)
        Slots  = $([int]$Counts.Slots)
        Host   = $([int]$Counts.Host)
    }
}
"@
}

$violations = New-Object System.Collections.Generic.List[string]
$counts = Get-Counts -Base $Root

if ($WriteBaseline) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $baselinePath) -Force | Out-Null
    Set-Content -LiteralPath $baselinePath -Value (New-Baseline -Counts $counts) -Encoding UTF8
    Write-Output "WRITE UiSingleEntry baseline => $baselineRel"
    exit 0
}

$baseline = if (Test-Path -LiteralPath $baselinePath) { Import-PowerShellDataFile -LiteralPath $baselinePath } else { $null }

# ---- C0 登记在位 ----
$registrationPath = Join-Path $Root $registrationRel
if (-not (Test-Path -LiteralPath $registrationPath)) {
    $violations.Add('[C0] GateManifests.psd1 缺失')
} else {
    $registrationText = Get-Content -LiteralPath $registrationPath -Raw
    if ($registrationText -notmatch 'UiSingleEntry\s*=\s*@\{') {
        $violations.Add('[C0] UiSingleEntry 未登记于 GateManifests.psd1')
    }
}

# ---- C1-C3 计数棘轮 ----
if ($null -eq $baseline) {
    $violations.Add("[C1-C3] 基线缺失：$baselineRel（修复：-WriteBaseline）")
} else {
    foreach ($name in $patterns.Keys) {
        $limit = [int]$baseline.SingleEntry[$name]
        $actual = [int]$counts[$name]
        if ($actual -gt $limit) {
            $violations.Add("[C1-C3] $name 静态槽直取上升：actual=$actual baseline=$limit（只降不升）")
        }
        if ($ShowDetail) { Write-Output ("DETAIL {0}: actual={1} baseline={2}" -f $name, $actual, $limit) }
    }
}

# ---- C4 目录无 LegacyRoute ----
$catalogPath = Join-Path $Root $catalogRel
if (-not (Test-Path -LiteralPath $catalogPath)) {
    $violations.Add("[C4] 目录生成物缺失：$catalogRel")
} else {
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $legacy = @($catalog.commands | ForEach-Object { $_.ops } | Where-Object { $_.handler -eq 'LegacyRoute' }).Count
    if ($legacy -gt 0) {
        $violations.Add("[C4] 目录仍有 handler=LegacyRoute 行：$legacy")
    }

    # ---- C5 handler 闭合 ----
    foreach ($command in $catalog.commands) {
        foreach ($op in $command.ops) {
            if ([string]::IsNullOrWhiteSpace($op.handler)) {
                $violations.Add("[C5] $($command.action).$($op.op) 缺 handler=")
            } elseif ($handlerVocabulary -notcontains $op.handler) {
                $violations.Add("[C5] $($command.action).$($op.op) 的 handler '$($op.handler)' 不在领域 handler 词表内")
            }
        }
    }
}

# ---- C7 正对照（判据有标的）----
$total = [int]$counts.Access + [int]$counts.Slots + [int]$counts.Host
if ($total -le 0) {
    $violations.Add('[C7] 检测器当前 0 命中：判据无标的（符号清单被掏空 ⇒ 判红，禁假绿）')
}

# ---- C6 负对照 ----
if ($NegativeControl) {
    $samples = @(
        'private void X() => AgentUiHostAccess.Access.Conversation;',
        'var s = AgentServiceSlots.Resolve("x", null);',
        'AgentUiHost.EnsureConversationReadyAsync();',
        'AgentUiHostAccess.Access.Framework.Database;',
        'AgentUiHost.TaskPoolSlot.Attach(pool);'
    )
    foreach ($sample in $samples) {
        $hit = $false
        foreach ($name in $patterns.Keys) {
            if ([regex]::IsMatch($sample, $patterns[$name])) { $hit = $true; break }
        }
        if (-not $hit) { $violations.Add("[C6] 负对照未被判红：$sample") }
    }
    $clean = 'var connector = UiConnectorHost.Current;'
    foreach ($name in $patterns.Keys) {
        if ([regex]::IsMatch($clean, $patterns[$name])) { $violations.Add('[C6] 负对照正样本被误判红') }
    }
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Output $v }
    if ($Check) { Write-Output ('FAIL UiSingleEntry: ' + $violations.Count + ' 项'); exit 1 }
    Write-Output ('WARN UiSingleEntry: ' + $violations.Count + ' 项（报告模式）')
    exit 0
}

Write-Output 'PASS UiSingleEntry (C0 登记 / C1-C3 棘轮 / C4 LegacyRoute=0 / C5 handler 闭合 / C6 负对照 / C7 正对照)'
exit 0
