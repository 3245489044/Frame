#requires -Version 5.1
<#
.SYNOPSIS
    Agent 通用框架单源守卫（AgentFrameworkSingleSource）—— 四个横切关注点框架的机器判据。

.DESCRIPTION
    判据（基线全部外置到 Baselines\AgentFrameworkSingleSource.psd1；事实行 = @{ Value; Origin; DueWhen }）：
      D1 诊断载体单源：Operation.HierarchyMethod 内 `Action<string, object?>` 只允许出现在
                        Abstractions\IDiagnosticsSink.cs（适配器本体）；其余文件 0。
      D2 回退姿态单点：流回退分支（_taskProvisionFlow/_attributionFlow 的 is null/is not null）
                        按文件 ≤ 基线（只减不增）；且 FlowPosture 确实被使用（正对照）。
      D3 键寻址收口：命名空间取值点（NodeOutputKeys.Normalize( / NodeKeys.DefaultWorkflowId）
                        按文件 ≤ 基线（只减不增）；KeyAddressing 模块存在且被消费。
      D4 簿记流模板：BookkeepingFlowCatalog 行数 ≥ 基线；每行 FileName ↔ .ushar 存在
                        且该文件声明 [Flow] <FlowName>（与 DeclaredFlowIds 对账一致）。
      D5 参数读取单源：*OpsFunctions.cs 内不再出现「Parameters.TryGetValue」形态的私有取参实现。
      D6 正对照（防假绿）：扫描面命中总数 > 0。
      D7 负对照（防假绿）：合成违规样本必须被 D1/D3 判据命中。
      D8 簿记流执行器构造点单源（§6 Wiring 切片）：`new OneShotFlowRunner(` 构造点 ≤ 基线（唯一 =
                        BookkeepingFlowAssembly）；且该工厂确有消费点（正对照）。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NodeGateCommon.ps1')

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

# 基线单源 = Baselines\AgentFrameworkSingleSource.psd1（禁内联数值基线；禁第二基线根；守卫 = Operation\DSL\Check-GateBaselines.ps1）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$b = Import-GateBaseline -Name AgentFrameworkSingleSource
# 事实行解包（U9：每键须为 @{Value;Origin;DueWhen}）⇒ 脚本内零裸基线值
$DiagnosticsCarrierAllowedFiles   = @($b.DiagnosticsCarrierAllowedFiles.Value)
$KeyAddressingBaselines           = $b.KeyAddressingBaselines.Value
$FlowFallbackBaselines            = $b.FlowFallbackBaselines.Value
$BookkeepingFlowRows              = [int]$b.BookkeepingFlowRows.Value
$ParamImplementationMax           = [int]$b.ParamImplementationMax.Value
$BookkeepingRunnerConstructionMax = [int]$b.BookkeepingRunnerConstructionMax.Value
$PostureRequireFlowRequired       = [bool]$b.PostureRequireFlowRequired.Value
$PostureRequireFlowMin            = [int]$b.PostureRequireFlowMin.Value
$ScanSurfaceMinHits               = [int]$b.ScanSurfaceMinHits.Value
$ContextAssemblyComboBaseline     = [int]$b.ContextAssemblyComboBaseline.Value
$LocalFlowMsgIdMax                = [int]$b.LocalFlowMsgIdMax.Value
$violations = [System.Collections.Generic.List[string]]::new()

$hmDir      = Join-Path $Root 'Frame\CsharpPrj\Operation\HierarchyMethod'
$agentSrc   = Join-Path $Root 'Projects\Plug\Agent\src'
$catalog    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\Bookkeeping\BookkeepingFlowCatalog.cs'
$workflows  = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
$specCatalog= Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs'

$DiagCarrierPattern = 'Action<string,\s*object\?>'
$FlowBranchPattern  = '_(taskProvisionFlow|attributionFlow|roundAttributionFlow|productionsFlow)\s+(is\s+null|is\s+not\s+null)'
$NamespacePattern   = 'NodeOutputKeys\s*\.\s*Normalize\s*\(|NodeKeys\s*\.\s*DefaultWorkflowId'
$ParamImplPattern   = 'Parameters\.TryGetValue'

Write-GateHeader -Title 'Agent 通用框架单源守卫（诊断 / 回退姿态 / 键寻址 / 簿记流模板 / 姿态收口台账）'

$totalHits = 0

# ---------------- D1 诊断载体单源 ----------------
$diagCounts = Get-PatternFileCounts -Dirs @($hmDir) -Pattern $DiagCarrierPattern
foreach ($f in $diagCounts.Keys) {
    if ($DiagnosticsCarrierAllowedFiles -notcontains $f) {
        $violations.Add("D1 诊断载体散落：$f（只允许 $($DiagnosticsCarrierAllowedFiles -join ', ')；一律经 IDiagnosticsSink）")
    }
}
$totalHits += ($diagCounts.Values | Measure-Object -Sum).Sum

# ---------------- D2 回退姿态 ----------------
$flowCounts = Get-PatternFileCounts -Dirs @($hmDir, $agentSrc) -Pattern $FlowBranchPattern
foreach ($v in (Test-BaselineNoIncrease -Counts $flowCounts -Baselines $FlowFallbackBaselines -Label 'D2 回退分支')) {
    $violations.Add($v)
}
$postureRefs = Get-PatternFileCounts -Dirs @($hmDir) -Pattern '_posture\.RunAsync|FlowPosture\.AllowLegacyFallback|FlowPosture\.RequireFlow'
if (($postureRefs.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add('D2 正对照失效：FlowPosture 无任何使用点（框架未接线？）')
}
$totalHits += ($flowCounts.Values | Measure-Object -Sum).Sum

# ---------------- D3 键寻址收口 ----------------
$nsCounts = Get-PatternFileCounts -Dirs @($hmDir, $agentSrc) -Pattern $NamespacePattern
foreach ($v in (Test-BaselineNoIncrease -Counts $nsCounts -Baselines $KeyAddressingBaselines -Label 'D3 命名空间取值点')) {
    $violations.Add($v)
}
$keyAddrFile = Join-Path $hmDir 'Nodes\Keys\KeyAddressing.cs'
if (-not (Test-Path -LiteralPath $keyAddrFile)) {
    $violations.Add('D3 键寻址模块缺失：Nodes\Keys\KeyAddressing.cs')
}
$keyAddrRefs = Get-PatternFileCounts -Dirs @($hmDir, $agentSrc) -Pattern 'KeyAddressing\.(DefaultNamespace|NormalizeNamespace|ContainerId|ContainerName)'
if (($keyAddrRefs.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add('D3 正对照失效：KeyAddressing 无任何消费点（模块未被使用？）')
}
$totalHits += ($nsCounts.Values | Measure-Object -Sum).Sum


# ---------------- D4 簿记流模板 ----------------
$specConsts = Get-CSharpConstMap -Path $specCatalog
$flowIds = Get-DeclaredFlowIds -Path $specCatalog
$rows = @()
if (-not (Test-Path -LiteralPath $catalog)) {
    $violations.Add('D4 簿记流目录缺失：BookkeepingFlowCatalog.cs')
} else {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $catalog -Raw),
            'WorkflowSpecFiles\.(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*"(?<f>[A-Za-z0-9]+)"')) {
        $rows += [PSCustomObject]@{ Const = $m.Groups['c'].Value; Flow = $m.Groups['f'].Value }
    }
}
if ($rows.Count -lt $BookkeepingFlowRows) {
    $violations.Add("D4 簿记流行数不足：$($rows.Count) < 基线 $BookkeepingFlowRows（数据表被删？）")
}
foreach ($row in $rows) {
    if (-not $specConsts.ContainsKey($row.Const)) {
        $violations.Add("D4 流常量未登记：WorkflowSpecFiles.$($row.Const)")
        continue
    }
    $base = $specConsts[$row.Const] -replace '\.ushar$', ''
    $path = Join-Path $workflows "$base.ushar"
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("D4 声明面缺失：$base.ushar（簿记流目录已登记流号 $($row.Flow)）")
        continue
    }
    if (-not (Select-String -LiteralPath $path -Pattern "\[Flow\]\s+$($row.Flow)([:：\s|]|$)" -Quiet)) {
        $violations.Add("D4 流号漂移：$base.ushar 未声明 [Flow] $($row.Flow)")
    }
    if (-not $flowIds.ContainsKey($base)) {
        $violations.Add("D4 流号未登记：DeclaredFlowIds 缺 $base")
    } elseif ($flowIds[$base] -ne $row.Flow) {
        $violations.Add("D4 流号不一致：DeclaredFlowIds[$base]=$($flowIds[$base]) ≠ 目录 $($row.Flow)")
    }
}
$totalHits += $rows.Count

# ---------------- D5 参数读取单源 ----------------
# 只审 fn 实现文件（*OpsFunctions.cs）：取参逻辑须经 WorkflowContextParams，禁本层重写实现。
$paramCounts = Get-PatternFileCounts -Dirs @($agentSrc) -Pattern $ParamImplPattern
$paramTotal = 0
foreach ($k in $paramCounts.Keys) {
    if ($k -like '*OpsFunctions.cs') { $paramTotal += $paramCounts[$k] }
}
if ($paramTotal -gt $ParamImplementationMax) {
    $violations.Add("D5 取参实现重复：OpsFunctions 命中 $paramTotal > 上限 $ParamImplementationMax（须经 WorkflowContextParams）")
}
$wcRefs = Get-PatternFileCounts -Dirs @($agentSrc) -Pattern 'WorkflowContextParams\.(Get|GetInt|TryGet)'
if (($wcRefs.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add('D5 正对照失效：WorkflowContextParams 无任何消费点')
}

# ---------------- D8 簿记流执行器构造点单源 ----------------
# 判据：簿记流执行器的构造点唯一（BookkeepingFlowAssembly）—— 缩减切片口径（无写透/键映射/投影器/Outbox
# 且切片内 AttributionFlow=null 防递归）不得在各装配点逐字重写。
$runnerCounts = Get-PatternFileCounts -Dirs @($agentSrc) -Pattern 'new OneShotFlowRunner\('
$runnerTotal = ($runnerCounts.Values | Measure-Object -Sum).Sum
if ($runnerTotal -gt $BookkeepingRunnerConstructionMax) {
    $violations.Add("D8 簿记流执行器构造点重复：$runnerTotal > 上限 $BookkeepingRunnerConstructionMax（命中：$([string]::Join('、', @($runnerCounts.Keys)))；须经 BookkeepingFlowAssembly）")
}
$sliceRefs = Get-PatternFileCounts -Dirs @($agentSrc) -Pattern 'BookkeepingFlowAssembly\.(RunnerOf|RunnerFor)'
if (($sliceRefs.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add('D8 正对照失效：BookkeepingFlowAssembly 无任何消费点（切片单源未接线？）')
}
$totalHits += $runnerTotal

# ---------------- D9 姿态收口台账（批次 A：报告口径 + 可翻转判据） ----------------
# 目的：把「FlowPosture 是否已从迁移期切到收口」从注释变成机器可判的观测事实。
#   迁移期（PostureRequireFlowRequired=$false）：只报告计数（台账），不判违规；
#   收口期（$true）：组合根须至少 PostureRequireFlowMin 处显式选定 RequireFlow。
# 计数面 = 非注释行的 `FlowPosture.RequireFlow` / `FlowPosture.AllowLegacyFallback` 出现次数。
$requireFlowCount = 0
$allowFallbackCount = 0
foreach ($dir in @($hmDir, $agentSrc)) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    $d9Files = Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' -and $_.FullName -notlike '*\BuildOutput\*' }
    foreach ($f in $d9Files) {
        foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
            if ($line.TrimStart() -match '^(//|/\*|\*)') { continue }
            if ($line -match 'FlowPosture\.RequireFlow') { $requireFlowCount++ }
            if ($line -match 'FlowPosture\.AllowLegacyFallback') { $allowFallbackCount++ }
        }
    }
}
# 正对照（D9 自身）：回退命中必须可观测（诊断码载体在位），否则「收口是否生效」无据。
$fallbackDiagRefs = Get-PatternFileCounts -Dirs @($hmDir) -Pattern 'DiagnosticsCodes\.FlowFallback'
if (($fallbackDiagRefs.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add('D9 正对照失效：DiagnosticsCodes.FlowFallback 无使用点（回退命中不可观测，收口无据？）')
}
if ($PostureRequireFlowRequired -and $requireFlowCount -lt $PostureRequireFlowMin) {
    $violations.Add("D9 姿态未收口：FlowPosture.RequireFlow 装配点 $requireFlowCount < $PostureRequireFlowMin（PostureRequireFlowRequired=$PostureRequireFlowRequired）")
}
$totalHits += $requireFlowCount

# ---------------- D6 正对照 ----------------
if ($totalHits -lt $ScanSurfaceMinHits) {
    $violations.Add("D6 正对照失效：扫描面命中 $totalHits < $ScanSurfaceMinHits（扫描面或模式失效？）")
}

# ---------------- D7 负对照 ----------------
$probe = Join-Path ([System.IO.Path]::GetTempPath()) ("afs-probe-" + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $probe -Force | Out-Null
try {
    $badPath = Join-Path $probe 'Bogus.cs'
    Set-Content -LiteralPath $badPath -Encoding UTF8 -Value @(
        'public sealed class Bogus {',
        '    private readonly Action<string, object?>? _log;',
        '    public string X() => NodeOutputKeys.Normalize(null);',
        '    public object Y() => new OneShotFlowRunner(null!, null!);',
        '}')
    $probeDiag = Get-PatternFileCounts -Dirs @($probe) -Pattern $DiagCarrierPattern
    if (-not $probeDiag.ContainsKey('Bogus.cs')) {
        $violations.Add('D7 负对照失效：D1 判据对合成违规未命中（判据恒真？）')
    }
    $probeNs = Get-PatternFileCounts -Dirs @($probe) -Pattern $NamespacePattern
    if (-not $probeNs.ContainsKey('Bogus.cs')) {
        $violations.Add('D7 负对照失效：D3 判据对合成违规未命中（判据恒真？）')
    }
    $probeRunner = Get-PatternFileCounts -Dirs @($probe) -Pattern 'new OneShotFlowRunner\('
    if (-not $probeRunner.ContainsKey('Bogus.cs')) {
        $violations.Add('D7 负对照失效：D8 判据对合成违规未命中（判据恒真？）')
    }
} finally {
    Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------- D10 上下文组装组合点基线（只减不增） ----------------
# 口径（S6-c / 组装归流 S1）：Scope A（node/task/history 读组装）路径内「归属边枚举 + 节点输出逐键读」**组合**的
# 文件数 ≤ 基线。单一实现 = Agent.Session\Context\ContextProjection.cs（S1 已收口；本组合不得再现第二处实现）。
$comboFiles = [System.Collections.Generic.List[string]]::new()
foreach ($f in (Get-ChildItem -LiteralPath $agentSrc -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' })) {
    $hasEnumeration = $false
    $hasKeyedRead = $false
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $t = $line.Trim()
        if ($t.StartsWith('//') -or $t.StartsWith('/*') -or $t.StartsWith('*')) { continue }
        if ($line.Contains('GetTaskNodeKeysAsync')) { $hasEnumeration = $true }
        if ($line.Contains('TryGetNodeOutputAsync')) { $hasKeyedRead = $true }
    }
    if ($hasEnumeration -and $hasKeyedRead) { $comboFiles.Add($f.Name) }
}
if ($comboFiles.Count -gt $ContextAssemblyComboBaseline) {
    $violations.Add("D10 读组装组合点新增：$($comboFiles -join ', ') = $($comboFiles.Count) > 基线 $ContextAssemblyComboBaseline（O-1 触发点收口：只减不增）")
}
if ($comboFiles.Count -le 0) {
    $violations.Add('D10 正对照失效：读组装组合点扫描全 0（扫描面或模式失效？）')
}

# ---------------- D11 运行期本地流 msgId 单源（统一 DiagnosticsCodes + context） ----------------
# 口径：簿记流「缺失 / 失败」的**运行期**可见化一律经 DiagnosticsCodes（flow_entry_missing / flow_run_failed）+
#   context 标签承载站点，禁各调用点自造本地 msgId（口径漂移 + 检索对账面分裂）。
#   装配期可见化（Composition_*_unavailable / ConversationHosting_*_unavailable）与模板基类
#   （BookkeepingFlow_runner_missing）不在本判据面内（各为「装配一次 / 模板单源」）。
$LocalFlowMsgIdPattern = 'NodeStepExecutionService_AttributionFlow_missing|TaskOrchestrator_(AttributionFlow|TaskProvisionFlow)_(missing|failed)|ConversationPipeline_(roundAttribution|productions)_unavailable|HierarchyManager_provision_failed'
$localMsgCounts = Get-PatternFileCounts -Dirs @($hmDir, $agentSrc) -Pattern $LocalFlowMsgIdPattern
$localMsgTotal = ($localMsgCounts.Values | Measure-Object -Sum).Sum
if ($localMsgTotal -gt $LocalFlowMsgIdMax) {
    $violations.Add("D11 运行期本地流 msgId 未收口：$localMsgTotal > 上限 $LocalFlowMsgIdMax（命中：$([string]::Join('、', @($localMsgCounts.Keys)))；统一走 DiagnosticsCodes + context）")
}
# 正对照：统一编码确实被消费（否则「收口」无承载）。
$diagCodeRefs = Get-PatternFileCounts -Dirs @($hmDir) -Pattern 'DiagnosticsCodes\.FlowMissing|DiagnosticsCodes\.FlowFailed'
if (($diagCodeRefs.Values | Measure-Object -Sum).Sum -le 0) {
    $violations.Add('D11 正对照失效：DiagnosticsCodes.FlowMissing/FlowFailed 无使用点（收口无承载？）')
}
$totalHits += $localMsgTotal

# ---------------- 输出 ----------------
if ($ShowDetail -or $violations.Count -gt 0) {
    Write-Host ""
    Write-Host ("  D1 诊断载体文件 {0} · D2 回退分支 {1} · D3 命名空间取值点 {2} · D4 簿记流行 {3} · D5 取参实现 {4} · D6 扫描面命中 {5} · D8 簿记流执行器构造点 {6} · D9 姿态(RequireFlow={7}/AllowLegacy={8}) · D11 本地流 msgId {9}" -f `
        $diagCounts.Count, ($flowCounts.Values | Measure-Object -Sum).Sum, ($nsCounts.Values | Measure-Object -Sum).Sum,
        $rows.Count, $paramTotal, $totalHits, $runnerTotal, $requireFlowCount, $allowFallbackCount, $localMsgTotal) -ForegroundColor Gray
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'AgentFrameworkSingleSource'
