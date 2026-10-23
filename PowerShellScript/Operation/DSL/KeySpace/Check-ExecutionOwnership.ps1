# Check-ExecutionOwnership.ps1
# 执行归属门禁（K-03 / A3 / D1–D4 机检面）
#
# 判据（-Check 时违规即退出码 1；载体 = 调用点结构事实）：
#   EO1 建成员不驱动：TaskOrchestrator.CreateMemberTaskAsync 方法体内不得出现 ActivateTaskAsync 调用
#                      （建即激活 = 与宿主投递端口重复驱动）。
#   EO2 驱动取令牌：TaskOrchestrator.ActivateTaskAsync 必须调用 IExecutionOwnership.TryAcquire。
#   EO3 内核在位：Modules\Governance\Ownership 的归属内核文件必须存在并被引用。
#   EO4 负对照：合成「建成员即激活」样本必须命中 EO1（判据非空转）。
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

# ---- EO5：注册面分区 + 装配冻结（F2；判据载体 = 调用点/定义结构事实）----
$gateFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Runtime\State\Orchestration\RegistrationGate.cs'
$registrarFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Common\FunctionRegistrar.cs'
$assemblyFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Composition\Hosting\AgentWorkflowFunctionRegistrar.cs'

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

if (-not (Test-Path -LiteralPath $assemblyFile)) {
    $violations.Add('EO5 装配点缺失：AgentWorkflowFunctionRegistrar.cs')
} elseif ((Get-Content -LiteralPath $assemblyFile -Raw) -notmatch 'FreezeDeclared\s*\(\s*\)') {
    $violations.Add('EO5 装配点未冻结声明面：ValidateRegisteredFunctions 缺 FreezeDeclared() 调用')
}

if ($NegativeControl) {
    # EO4/EO5 负对照：合成样本必须命中各自断言模式（判据非空转）
    $freezeSample = 'FunctionRegistrar.FreezeDeclared();'
    if ($freezeSample -notmatch 'FreezeDeclared\s*\(\s*\)') {
        $violations.Add('EO5 负对照失败：装配冻结样本未命中')
    }
}

if ($NegativeControl) {
    $sample = 'public async Task CreateMember() { await ActivateTaskAsync(key); }'
    if ($sample -match 'await\s+ActivateTaskAsync\s*\(') {
        # 命中即符合预期（判据非空转）
    } else {
        $violations.Add('EO4 负对照失败：建成员激活样本未命中')
    }
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'ExecutionOwnership'
