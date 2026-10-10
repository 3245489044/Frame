#requires -Version 5.1
<#
.SYNOPSIS
    Agent 编排归流守卫（AgentOrchestrationFlow）—— 「编排归流」的机器判据（纲领 O-1…O-6 / R6）。

.DESCRIPTION
    沿《Agent 全量工作流化-编排归流纲领》§5（O-1…O-6）与 §7 R6：
      · 每个转换/关注点的**编排真源恰一条 `[Flow]`**（声明面 `.ushar`）；
      · `fn=` 单步执行体只算/只调原语（不在函数体内编排）；
      · `fn=` 在启动期校验清单内（豁免须显式登记）；
      · 载体分工唯一（同一关注点不得两种载体承载）；
      · 缺失即抛 / 可见化（禁静默 skip）。

    扫描面：
      Projects\Plug\Agent\Config\workflows\*.ushar                （编排真源声明面）
      Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs   （文件名/流号单源）
      Projects\Plug\Agent\src\Agent.Contract\WorkflowFunction\WorkflowFunctionNames.cs（fn 名单源）
      Projects\Plug\Agent\src\Agent.Core\Nodes\Registry\LoopParadigms.cs               （R5 范式登记）
      Projects\Plug\Agent\src\Agent.Launcher\Loading\WorkflowFunctionRefValidator.cs   （O-3 清单）
      Projects\Plug\Agent\src\Agent.Modules\{Attribution,Provision,History,RoundAttribution,Productions}\*OpsFunctions.cs（O-2 单步体）
      Projects\Plug\Agent\src\Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs（O-5 静默基线）
      Projects\Plug\Agent\doc\Agent编排载体分工表.md                                   （O-6 载体分工 + 豁免登记）
      Projects\Plug\Agent\src\Agent.Axioms\Loops\RoundKeys.cs                          （E10 键名常量单源）
      Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\RoundStateKeyCatalog.cs    （E10 种子键登记）
      Projects\Plug\Agent\Config\workflows\{attribution,task-provision}.ushar           （E10 inports= 对账面）
      Frame\CsharpPrj + Projects\Plug\Agent\src\**\*.cs                                 （E11/E12 调用点与命名空间取值点基线）

    判据：
      E1  闸A 编译：R1/R2/R5 三条新增声明经 Usharp.Cli build ⇒ 错误 0（无悬空引用 / 形状非法）
      E2  编排真源对账：`DeclaredFlowIds` 每项 basename ↔ `Config/workflows/{basename}.ushar` 存在
                        且该文件含 `[Flow] <映射流号>`（治「声明改了不生效」）
      E3  `fn=` 名单源对账：校验清单内文件的 `[Node] fn=` 值 ∈ `WorkflowFunctionNames` 常量值集
                        （豁免白名单 = 编排性阶段名；豁免文件清单 = 不进校验清单的声明文件）
      E4  范式登记对账（R5）：`LoopParadigms` 登记的 ExecutorId/WorkflowFile/FlowId ↔ 声明面存在
                        + `DeclaredFlowIds` 映射一致（`ExecutorId ↔ [Flow]` 一一对应）
      E5  O-2 单步体扫描：fn 实现文件内不得出现编排关键词（retry / Task.Delay / WhenAll / Attempt）；
                        且须含 ≥1 原语调用（正对照）
      E6  O-6 载体分工表：文档存在且含 §1 三载体 / §2 分工表 / §3 豁免登记 / §4 范式登记（+ E1–E4 豁免条目）
      E7  O-5 静默降级基线（只减不增）：`ConversationCentralPipeline.cs` 的 `Spec is null) return;` ≤ 基线
      E8  正对照（防假绿）：声明流数基线与 fn 常量数下限
      E9  负对照（防假绿）：对合成违规样本执行同一判据函数，必须命中
      E10 簿记入参三方对账（ERR-6）：`Config/workflows/{attribution,task-provision}.ushar` 的 `inports=`
                         ⊆ RoundKeys 常量值 ∧ 其常量名 ∈ RoundStateKeyCatalog.CompatSeedKeys
                        （漏登记 ⇒ 步骤 inports 解析为空串 = 功能静默失效）
      E11 归属边/供给/挂载调用点数基线（只减不增）：`LinkNodeAsync` / `EnsureTaskNodeLinkAsync`
                        / `EnsureHistoryAsync` / `AttachHistoryTaskAsync` / `AddSessionMessageLinkAsync`
                        五组成员访问调用点（O-1 触发点的机器判据）
      E12 task→node 边命名空间取值点基线（只减不增）：边写侧（SyncRelationsBlock / HistoryProjector）
                        的 `NodeOutputKeys.Normalize(` + `NodeKeys.DefaultWorkflowId` 取值点（ERR-3）

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（E1–E9）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-AgentOrchestrationFlow.ps1 -ShowDetail

.EXAMPLE
    .\Check-AgentOrchestrationFlow.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
# 公共扫描器/基线库（§8 门禁框架）：取数原语与取值点扫描器单源，门禁脚本只留判据。
. (Join-Path $PSScriptRoot 'NodeGateCommon.ps1')
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# ---------------- 基线（只减不增；变更须给出理由并同步本表 + 载体分工表 §3） ----------------
$workflowsDir   = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
$specCatalog    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs'
$fnNamesFile    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\WorkflowFunction\WorkflowFunctionNames.cs'
$paradigmsFile  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\Registry\LoopParadigms.cs'
$validatorFile  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Loading\WorkflowFunctionRefValidator.cs'
$pipelineFile   = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs'
$carrierDoc     = Join-Path $Root 'Projects\Plug\Agent\doc\Agent编排载体分工表.md'
$roundKeysFile  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Axioms\Loops\RoundKeys.cs'
$seedCatalogFile= Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\RoundStateKeyCatalog.cs'
$cliExe         = Join-Path $Root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
$cliProj        = Join-Path $Root 'Frame\Usharp\src\Usharp.Cli\Usharp.Cli.csproj'

# R1/R2/R5/B4/DEBT-4 新增声明面（闸A 编译对象）
$newDeclarationFiles = @('attribution.ushar', 'task-provision.ushar', 'stateless-answer.ushar', 'session-attribution.ushar', 'round-attribution.ushar', 'session-productions.ushar', 'task-lifecycle.ushar', 'task-advance.ushar', 'task-compose.ushar', 'history-merge.ushar', 'history-detach.ushar')

# fn 豁免白名单（编排性阶段名：非引擎注册函数，纳入校验语义不成立）
$FnExemptNames = @('initialize', 'load', 'process', 'save', 'complete')
# 声明文件豁免清单（不进 `WorkflowFunctionRefValidator.WorkflowFiles`；理由见载体分工表 §3）
# RuntimeGovernance.ushar 为「非骨架声明面」（只声明词表/端口契约/矩阵，不产 RoundSpec 骨架；ERR-4B 补登记）
$ValidatorExemptFiles = @('session-lifecycle.ushar', 'stateless-answer.ushar', 'RuntimeGovernance.ushar')

# 基线单源 = Baselines\AgentOrchestrationFlow.psd1（禁内联数值基线；守卫 = Operation\DSL\Check-GateBaselines.ps1）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name AgentOrchestrationFlow
# 声明面基线（防声明被删而门禁恒绿）；O-5 静默降级基线（只减不增）
$DeclaredFlowCountBaseline = [int]$g.DeclaredFlowCount.Value
$FnConstCountBaseline      = [int]$g.FnConstCount.Value
$SilentSkipBaseline        = [int]$g.SilentSkip.Value
# E10 簿记入参对账面（声明面 inports= 的对账对象；新增簿记流须同步本表）
$InportAuditFiles = @('attribution.ushar', 'task-provision.ushar', 'session-attribution.ushar', 'round-attribution.ushar', 'context-projection.ushar', 'task-lifecycle.ushar', 'task-advance.ushar', 'task-compose.ushar', 'history-merge.ushar', 'history-detach.ushar')
# E10 收集式输入端口白名单（**逐字**写 token；来源 = NodeExecutionKeys.CollectNodePorts，
#   判据族见 Check-NodeCollectPort C1–C5）：该 token 是「收集端口位置」标记，不是键值 ⇒ 不参与 RoundKeys / CompatSeedKeys 对账；
#   非收集位置的 inports 仍须 ∈ RoundKeys ∧ ∈ CompatSeedKeys（本白名单**仅**对收集端口位置生效）。
$CollectPortTokens = @('nodes.*')
# E11 调用点数基线（只减不增；实测于 R1–R6 落地后；目标值见括号）
#   白名单内允许的调用点 = 流内 fn 实现 + 端口适配实现 + 唯一写边守卫；收口改码后须下调本表
$CallSiteBaselines = [ordered]@{
    'LinkNodeAsync'              = 2   # 实测 2（B2 续：SyncRelationsBlock 旧路径删除；余 HistoryProjector/NodeTaskHistoryService 迁移期回退）
    'EnsureTaskNodeLinkAsync'    = 4   # 实测 4（B2 续：NodeStepExecutionService 回退支删除）；目标 2 待 B9 测试面端口迁移（NodeTaskLinker）
    'EnsureHistoryAsync'         = 3   # 实测 3（B2 续：TaskOrchestrator R2 回退支删除）
    'AttachHistoryTaskAsync'     = 2   # 实测 2（B2 续：TaskOrchestrator R2 回退支删除）
    'AddSessionMessageLinkAsync' = 2   # 实测 2（B4/DUP-5 收口：入史挂载 + UI 运维两条直调改经 session-attribution 流；余 HistoryRelationManager 适配 + 流内 fn）
}
# E12 task→node 边命名空间取值点基线（只减不增；目标 = 单源 1）
$NamespaceBaselines = [ordered]@{
    'SyncRelationsBlock.cs' = 1   # B2 续已减 2（两条回退支删除）；目标 1（已成）
    'HistoryProjector.cs'   = 1   # RED-2 已减 1（ProjectMarkedAsync 死代码删除）；目标 1（已成）
}
$NamespaceValuePattern = 'NodeOutputKeys\s*\.\s*Normalize\s*\(|NodeKeys\s*\.\s*DefaultWorkflowId'
# O-2 编排关键词黑名单（fn 实现体内禁出现）
$OrchestrationKeywords = @('retry', 'Task.Delay', 'WhenAll', 'Attempt')

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Agent 编排归流守卫（O-1…O-6：编排真源 + 单步执行体 + 载体分工）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $workflowsDir)) {
    Write-Host "[FAIL] 未找到声明面目录：$workflowsDir" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------------- 判据函数（E3；E9 负对照复用） ----------------
function Test-FnNamesDeclared {
    param([string]$UsharPath, [string[]]$FnConstValues)
    $bad = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $UsharPath)) { return $bad }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $UsharPath -Raw), '\[Node\]\s*(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*fn=(?<fn>[A-Za-z_][A-Za-z0-9_]*)')) {
        $fn = $m.Groups['fn'].Value
        if ($FnExemptNames -contains $fn) { continue }
        if ($FnConstValues -notcontains $fn) { $bad.Add("$($m.Groups['id'].Value):$fn") }
    }
    return $bad
}

# ---------------- 判据函数（E11 调用点数 / E12 命名空间取值点：扫描器见 NodeGateCommon.ps1） ----------------
# ---------------- 取数 ----------------
$specConsts     = Get-CSharpConstMap -Path $specCatalog
$flowIds        = Get-DeclaredFlowIds -Path $specCatalog
$fnConstValues  = @(Get-CSharpConstMap -Path $fnNamesFile).Values
$validatorFiles = @(Get-WorkflowFiles -Path $validatorFile -SpecConsts $specConsts)

# ---------------- E1 闸A 编译 ----------------
function Invoke-UsharpBuild {
    param([string]$Path)
    if (Test-Path -LiteralPath $cliExe) { return (& $cliExe build $Path 2>&1 | Out-String) }
    if (Test-Path -LiteralPath $cliProj) { return (& dotnet run --project $cliProj -- build $Path 2>&1 | Out-String) }
    throw "未找到 Usharp 编译器（$cliExe 或 $cliProj）：闸A 判据 E1 不可判，禁静默通过"
}

$e1Pass = 0
foreach ($file in $newDeclarationFiles) {
    $path = Join-Path $workflowsDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("E1 声明面缺失：$file")
        continue
    }
    $output = Invoke-UsharpBuild -Path $path
    if ($output -match '错误\s*[:：]?\s*(?<n>[1-9][0-9]*)') {
        $violations.Add("E1 闸A 编译报错：$file → $($Matches['n']) 个错误")
    } else {
        $e1Pass++
    }
}

# ---------------- E2 编排真源对账 ----------------
$e2Checked = 0
foreach ($base in $flowIds.Keys) {
    $expectedFlow = $flowIds[$base]
    $path = Join-Path $workflowsDir "$base.ushar"
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("E2 声明文件缺失：$base.ushar（DeclaredFlowIds 已登记 $expectedFlow）")
        continue
    }
    $e2Checked++
    if (-not (Select-String -LiteralPath $path -Pattern "\[Flow\]\s+$expectedFlow([:：\s|]|$)" -Quiet)) {
        $violations.Add("E2 流号漂移：$base.ushar 未声明 [Flow] $expectedFlow")
    }
}

# ---------------- E3 fn 名单源对账 ----------------
$e3Checked = 0
foreach ($file in $validatorFiles) {
    $path = Join-Path $workflowsDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("E3 校验清单文件缺失：$file")
        continue
    }
    $e3Checked++
    foreach ($bad in (Test-FnNamesDeclared -UsharPath $path -FnConstValues $fnConstValues)) {
        $violations.Add("E3 fn 未在 WorkflowFunctionNames 登记：$file → $bad")
    }
}

# ---------------- E4 范式登记对账（R5） ----------------
$paradigmHits = 0
if (Test-Path -LiteralPath $paradigmsFile) {
    $paradigmRaw = Get-Content -LiteralPath $paradigmsFile -Raw
    foreach ($required in @('stateless-answer', 'cline', 'StatelessAnswer', 'AgentLoop')) {
        if ($paradigmRaw -notmatch [regex]::Escape($required)) {
            $violations.Add("E4 范式登记缺项：LoopParadigms 未出现 '$required'")
        } else { $paradigmHits++ }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $workflowsDir 'stateless-answer.ushar'))) {
        $violations.Add("E4 范式声明面缺失：stateless-answer.ushar")
    }
    if (-not $flowIds.ContainsKey('stateless-answer')) {
        $violations.Add("E4 范式流号未登记：DeclaredFlowIds 缺 'stateless-answer'")
    }
} else {
    $violations.Add("E4 范式登记文件缺失：LoopParadigms.cs")
}

# ---------------- E5 O-2 单步体扫描 ----------------
$fnImplFiles = [System.Collections.Generic.List[string]]::new()
foreach ($dir in @(
        'Projects\Plug\Agent\src\Agent.Modules\Attribution',
        'Projects\Plug\Agent\src\Agent.Modules\Provision',
        'Projects\Plug\Agent\src\Agent.Modules\History',
        'Projects\Plug\Agent\src\Agent.Modules\RoundAttribution',
        'Projects\Plug\Agent\src\Agent.Modules\Productions')) {
    $full = Join-Path $Root $dir
    if (Test-Path -LiteralPath $full) {
        foreach ($f in (Get-ChildItem -LiteralPath $full -Filter '*OpsFunctions.cs' -File)) { $fnImplFiles.Add($f.FullName) }
    }
}
$e5Checked = 0
foreach ($file in $fnImplFiles) {
    $e5Checked++
    $raw = Get-Content -LiteralPath $file -Raw
    foreach ($kw in $OrchestrationKeywords) {
        if ($raw -match [regex]::Escape($kw)) {
            $violations.Add("E5 单步体内出现编排关键词 '$kw'：$(Split-Path $file -Leaf)")
        }
    }
    # 正对照：须含原语调用（否则「零关键词」可能是空文件 / 未接线）；原语清单随扫描面扩面同步登记。
    if ($raw -notmatch 'CreateAsync|AttachTaskAsync|EnsureTaskNodeLinkAsync|AddSessionMessageLinkAsync|ComposeAsync|AppendAsync|MaterializeAsync|LinkTaskNodesAsync|SettleAsync') {
        $violations.Add("E5 单步体未见原语调用（正对照失效）：$(Split-Path $file -Leaf)")
    }
}

# ---------------- E6 O-6 载体分工表 + 豁免登记 ----------------
$carrierAnchors = @('三载体定义', '分工表', '豁免登记', '循环范式登记', 'E1', 'E2', 'E3', 'E4')
$e6Hits = 0
if (Test-Path -LiteralPath $carrierDoc) {
    $raw = Get-Content -LiteralPath $carrierDoc -Raw
    foreach ($anchor in $carrierAnchors) {
        if ($raw -match [regex]::Escape($anchor)) { $e6Hits++ }
        else { $violations.Add("E6 载体分工表缺锚点：'$anchor'") }
    }
} else {
    $violations.Add("E6 载体分工表缺失：$carrierDoc")
}

# ---------------- E7 O-5 静默降级基线（只减不增） ----------------
$silentHits = 0
if (Test-Path -LiteralPath $pipelineFile) {
    $silentHits = ([regex]::Matches((Get-Content -LiteralPath $pipelineFile -Raw), 'Spec\s+is\s+null\)\s*return;')).Count
    if ($silentHits -gt $SilentSkipBaseline) {
        $violations.Add("E7 静默降级新增：Spec 缺省跳过 $silentHits 处 > 基线 $SilentSkipBaseline（禁静默 skip；O-5）")
    }
} else {
    $violations.Add("E7 扫描面缺失：$pipelineFile")
}

# ---------------- E10 簿记入参三方对账（ERR-6：inports ⇄ RoundKeys ⇄ CompatSeedKeys） ----------------
$roundKeyConsts = Get-CSharpConstMap -Path $roundKeysFile
$keyValueToName = @{}
foreach ($k in $roundKeyConsts.Keys) { if (-not $keyValueToName.ContainsKey($roundKeyConsts[$k])) { $keyValueToName[$roundKeyConsts[$k]] = $k } }
$seedNames = [System.Collections.Generic.HashSet[string]]::new()
if (Test-Path -LiteralPath $seedCatalogFile) {
    $catalogRaw = Get-Content -LiteralPath $seedCatalogFile -Raw
    $compatMatch = [regex]::Match($catalogRaw, 'CompatSeedKeys\s*=\s*\{(?<body>.*?)\};', 'Singleline')
    if ($compatMatch.Success) {
        foreach ($m in [regex]::Matches($compatMatch.Groups['body'].Value, 'RoundKeys\.(?<n>[A-Za-z_][A-Za-z0-9_]*)')) {
            [void]$seedNames.Add($m.Groups['n'].Value)
        }
    } else {
        $violations.Add("E10 判据面缺失：RoundStateKeyCatalog 未找到 CompatSeedKeys 声明块")
    }
} else {
    $violations.Add("E10 判据面缺失：$seedCatalogFile")
}
$e10Checked = 0
foreach ($file in $InportAuditFiles) {
    $path = Join-Path $workflowsDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("E10 对账面缺失：$file")
        continue
    }
    $e10Checked++
    foreach ($bad in (Test-InportsRegistered -UsharPath $path -ValueToName $keyValueToName -SeedNames $seedNames -CollectTokens $CollectPortTokens)) {
        $violations.Add("E10 簿记入参未登记：$file → $bad（漏登记 ⇒ 步骤 inports 解析为空串，功能静默失效）")
    }
}

# ---------------- E11 调用点数基线（只减不增） ----------------
$callSiteCounts = @{}
try {
    $callSiteCounts = Get-CallSiteCounts -Dirs @(
        (Join-Path $Root 'Frame\CsharpPrj'),
        (Join-Path $Root 'Projects\Plug\Agent\src')
    ) -Members @($CallSiteBaselines.Keys)
    foreach ($member in $CallSiteBaselines.Keys) {
        if ($callSiteCounts[$member] -gt $CallSiteBaselines[$member]) {
            $violations.Add("E11 调用点新增：$member = $($callSiteCounts[$member]) > 基线 $($CallSiteBaselines[$member])（O-1 触发点收口：只减不增）")
        }
    }
} catch {
    $violations.Add("E11 判据不可判（扫描异常，禁静默通过）：$($_.Exception.Message)")
}

# ---------------- E12 task→node 边命名空间取值点基线（只减不增） ----------------
$namespaceCounts = @{}
try {
    $namespaceCounts = Get-NamespaceValueCounts -Dir (Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod') `
        -FileNames @($NamespaceBaselines.Keys) -Pattern $NamespaceValuePattern
    $nsAgentDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Orchestration\SessionWorkflow\NodeWrite'
    $nsAgentCounts = Get-NamespaceValueCounts -Dir $nsAgentDir -FileNames @($NamespaceBaselines.Keys) -Pattern $NamespaceValuePattern
    foreach ($f in @($NamespaceBaselines.Keys)) {
        $namespaceCounts[$f] = $namespaceCounts[$f] + $nsAgentCounts[$f]
    }
    foreach ($file in $NamespaceBaselines.Keys) {
        if ($namespaceCounts[$file] -gt $NamespaceBaselines[$file]) {
            $violations.Add("E12 命名空间取值点新增：$file = $($namespaceCounts[$file]) > 基线 $($NamespaceBaselines[$file])（O-4 事实单源：只减不增）")
        }
    }
} catch {
    $violations.Add("E12 判据不可判（扫描异常，禁静默通过）：$($_.Exception.Message)")
}

# ---------------- E8 正对照 ----------------
if ($flowIds.Count -lt $DeclaredFlowCountBaseline) {
    $violations.Add("E8 正对照失效：声明流 $($flowIds.Count) < 基线 $DeclaredFlowCountBaseline（声明被删？）")
}
if ($fnConstValues.Count -lt $FnConstCountBaseline) {
    $violations.Add("E8 正对照失效：fn 常量 $($fnConstValues.Count) < 基线 $FnConstCountBaseline（常量文件被清空？）")
}
# E11 正对照：扫描面须真实命中（全 0 = 扫描面失效 / 文件被清空，非「零违规」）
if (($callSiteCounts.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add("E8 正对照失效：E11 调用点扫描全 0（扫描面或模式失效？）")
}

# ---------------- E9 负对照（同一判据对合成违规必须命中） ----------------
$probePath = Join-Path ([System.IO.Path]::GetTempPath()) ("aof-probe-" + [guid]::NewGuid().ToString('n') + ".ushar")
try {
    Set-Content -LiteralPath $probePath -Value "[Node] bogus: fn=__no_such_fn__ | inports=x" -Encoding UTF8
    $probeBad = Test-FnNamesDeclared -UsharPath $probePath -FnConstValues $fnConstValues
    if (@($probeBad).Count -eq 0) {
        $violations.Add("E9 负对照失效：E3 判据对合成违规未命中（判据恒真？）")
    }
    # E10 负对照：未登记入参必须命中（判据恒真 / 恒假均失效）
    $probeInports = Test-InportsRegistered -UsharPath $probePath -ValueToName $keyValueToName -SeedNames $seedNames
    if (@($probeInports).Count -eq 0) {
        $violations.Add("E9 负对照失效：E10 判据对合成违规未命中（入参 'x' 未登记却放行？）")
    }
} finally {
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath -Force }
}

# ---------------- 输出 ----------------
$ok = $violations.Count -eq 0
Write-Host ""
Write-Host ("  编排真源流号 {0} · 校验清单文件 {1} · fn 常量 {2} · 范式命中 {3}" -f `
    $flowIds.Count, $validatorFiles.Count, $fnConstValues.Count, $paradigmHits) -ForegroundColor Gray

if ($ShowDetail -or -not $ok) {
    Write-Host ""
    Write-Host ("  E1 编译通过 {0}/{1} · E2 对账 {2} · E3 对账 {3} · E5 单步体 {4} · E6 锚点 {5}/{6} · E7 静默点 {7}" -f `
        $e1Pass, $newDeclarationFiles.Count, $e2Checked, $e3Checked, $e5Checked, $e6Hits, $carrierAnchors.Count, $silentHits) -ForegroundColor Gray
    Write-Host ("  E10 入参对账 {0} · E11 调用点 {1} · E12 命名空间取值点 {2}" -f `
        $e10Checked,
        (($CallSiteBaselines.Keys | ForEach-Object { "$_=$($callSiteCounts[$_])/$($CallSiteBaselines[$_])" }) -join ' '),
        (($NamespaceBaselines.Keys | ForEach-Object { "$_=$($namespaceCounts[$_])/$($NamespaceBaselines[$_])" }) -join ' ')) -ForegroundColor Gray
}

if ($ok) {
    Write-Host ""
    Write-Host "  [PASS] 编排归流守卫：0 违规（E1–E12）" -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host ""
Write-Host "  违规明细（$($violations.Count) 项）：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Yellow }
Write-Host ""
if ($Check) { exit 1 }
exit 0
