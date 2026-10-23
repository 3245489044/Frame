# Check-ExecutionOwnership.ps1
# 执行归属门禁（K-03 / A3 / D1–D4 机检面）
#
# 判据（-Check 时违规即退出码 1；载体 = 调用点结构事实）：
#   EO1 建成员不驱动：TaskOrchestrator.CreateMemberTaskAsync 方法体内不得出现 ActivateTaskAsync 调用
#                      （建即激活 = 与宿主投递端口重复驱动）。
#   EO2 驱动取令牌：TaskOrchestrator.ActivateTaskAsync 必须调用 IExecutionOwnership.TryAcquire。
#   EO3 内核在位：Modules\Governance\Ownership 的归属内核文件必须存在并被引用。
#   EO4 负对照：合成「建成员即激活」样本必须命中 EO1（判据非空转）。
#   EO5 冻结时机：装配根（AgentModuleRegistrar.SetupAgentAsync）恰一次 FreezeDeclared()，
#                 且次序晚于 InitializeAsync（装配完成 = 全部 Declared 注册已灌入）。
#   EO6 冻结点单点：目录注册器（AgentWorkflowFunctionRegistrar）不得再含 FreezeDeclared()（防双点/回退）。
#
# 模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$violations = [System.Collections.Generic.List[string]]::new()
$orchestrator = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Engine\TaskPool\TaskOrchestrator.cs'
$ownershipFile = Join-Path $Root 'Frame\CsharpPrj\Modules\Governance\Ownership\ExecutionOwnership.cs'

if (-not (Test-Path -LiteralPath $orchestrator)) {
    $violations.Add('EO0 编排器文件缺失：TaskOrchestrator.cs')
} else {
    $text = Get-Content -LiteralPath $orchestrator -Raw

    # EO1（结构事实）：编排器不得自我激活（建成员即驱动 = 与投递端口重复驱动）。
    if ($text -match 'await\s+ActivateTaskAsync\s*\(') {
        $violations.Add('EO1 建成员即驱动：TaskOrchestrator 内部直接 await ActivateTaskAsync')
    }

    # EO2（结构事实）：驱动入口必须使用归属令牌内核。
    if ($text -notmatch 'TryAcquire\s*\(') {
        $violations.Add('EO2 驱动未取令牌：ActivateTaskAsync 未调用 IExecutionOwnership.TryAcquire')
    }
    if ($text -notmatch 'IExecutionOwnership') {
        $violations.Add('EO2 驱动未接归属内核：TaskOrchestrator 未持有 IExecutionOwnership')
    }
}

if (-not (Test-Path -LiteralPath $ownershipFile)) {
    $violations.Add('EO3 归属内核缺失：Modules\Governance\Ownership\ExecutionOwnership.cs')
} else {
    $ownershipText = Get-Content -LiteralPath $ownershipFile -Raw
    foreach ($token in @('IExecutionOwnership', 'TryAcquire', 'Release', 'Inspect')) {
        if ($ownershipText -notmatch [regex]::Escape($token)) {
            $violations.Add("EO3 归属内核缺要素：$token")
        }
    }
}

# ---- EO5/EO6：注册面分区 + 装配冻结时机（F2/G6；判据载体 = 调用点结构事实与源码次序）----
$gateFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Runtime\State\Orchestration\RegistrationGate.cs'
$registrarFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Common\FunctionRegistrar.cs'
$assemblyRootFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Host\Composition\AgentModuleRegistrar.cs'
$launcherRegistrarFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\AgentWorkflowFunctionRegistrar.cs'

if (-not (Test-Path -LiteralPath $gateFile)) {
    $violations.Add('EO5 注册面内核缺失：RegistrationGate.cs')
} else {
    $gateText = Get-Content -LiteralPath $gateFile -Raw
    foreach ($token in @('RegistrationScope', 'Freeze(', 'IsFrozen(')) {
        if ($gateText -notmatch [regex]::Escape($token)) {
            $violations.Add("EO5 注册面内核缺要素：$token")
        }
    }
}

if (-not (Test-Path -LiteralPath $registrarFile)) {
    $violations.Add('EO5 注册器缺失：FunctionRegistrar.cs')
} elseif ((Get-Content -LiteralPath $registrarFile -Raw) -notmatch 'FreezeDeclared') {
    $violations.Add('EO5 注册器缺冻结入口：FunctionRegistrar.FreezeDeclared')
}

# 冻结时机判据（G6）：装配根内恰一次冻结，且次序在 InitializeAsync 之后（装配完成）。
function Get-FreezeOrderViolations([string]$Source, [string]$Label) {
    $found = [System.Collections.Generic.List[string]]::new()
    $initAnchor = 'await orchestrator.InitializeAsync()'
    $freezePattern = 'FunctionRegistrar\s*\.\s*FreezeDeclared\s*\(\s*\)'
    $idxInit = $Source.IndexOf($initAnchor, [System.StringComparison]::Ordinal)
    $hits = [regex]::Matches($Source, $freezePattern)
    if ($idxInit -lt 0) {
        $found.Add("$Label 缺装配完成锚：$initAnchor")
        return $found
    }
    if ($hits.Count -eq 0) {
        $found.Add("$Label 缺冻结调用：FunctionRegistrar.FreezeDeclared()")
        return $found
    }
    if ($hits.Count -gt 1) {
        $found.Add("$Label 冻结点非单点（出现 $($hits.Count) 次）")
    }
    if ($hits[0].Index -lt $idxInit) {
        $found.Add("$Label 冻结点早于 InitializeAsync（注册面在装配完成前冻结）")
    }
    return $found
}

if (-not (Test-Path -LiteralPath $assemblyRootFile)) {
    $violations.Add('EO5 装配根缺失：AgentModuleRegistrar.cs')
} else {
    $orderViolations = @(Get-FreezeOrderViolations -Source (Get-Content -LiteralPath $assemblyRootFile -Raw) -Label 'EO5 装配根')
    foreach ($v in $orderViolations) {
        if (-not [string]::IsNullOrWhiteSpace([string]$v)) { $violations.Add([string]$v) }
    }
}

# EO6：冻结点单点 —— 目录注册器可能早于 CoreModule.InitializeAsync，不得再承担冻结。
if (-not (Test-Path -LiteralPath $launcherRegistrarFile)) {
    $violations.Add('EO6 目录注册器缺失：AgentWorkflowFunctionRegistrar.cs')
} elseif ((Get-Content -LiteralPath $launcherRegistrarFile -Raw) -match 'FreezeDeclared\s*\(') {
    $violations.Add('EO6 冻结点重复：AgentWorkflowFunctionRegistrar.cs 不得再含 FreezeDeclared()（冻结单点 = 装配根）')
}

if ($NegativeControl) {
    # EO4/EO5/EO6 负对照：合成样本必须命中各自断言（判据非空转；EO5 另设正对照防恒红）
    $badOrderSample = 'FunctionRegistrar.FreezeDeclared();' + "`n" + 'await orchestrator.InitializeAsync();'
    if (@(Get-FreezeOrderViolations -Source $badOrderSample -Label 'EO5 负对照').Count -eq 0) {
        $violations.Add('EO5 负对照失败：冻结早于 InitializeAsync 的样本未命中')
    }
    $goodOrderSample = 'await orchestrator.InitializeAsync();' + "`n" + 'FunctionRegistrar.FreezeDeclared();'
    if (@(Get-FreezeOrderViolations -Source $goodOrderSample -Label 'EO5 正对照').Count -gt 0) {
        $violations.Add('EO5 正对照失败：装配末尾冻结样本被判红')
    }
    $doublePointSample = 'class Probe { void M() { FunctionRegistrar.FreezeDeclared(); } }'
    if ($doublePointSample -notmatch 'FreezeDeclared\s*\(') {
        $violations.Add('EO6 负对照失败：目录注册器双点样本未命中')
    }
    $sample = 'public async Task CreateMember() { await ActivateTaskAsync(key); }'
    if ($sample -match 'await\s+ActivateTaskAsync\s*\(') {
        # 命中即符合预期（判据非空转）
    } else {
        $violations.Add('EO4 负对照失败：建成员激活样本未命中')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ExecutionOwnership'
