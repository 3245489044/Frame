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
      Projects\Plug\Agent\src\Agent.Core\Nodes\LoopParadigms.cs                        （R5 范式登记）
      Projects\Plug\Agent\src\Agent.Launcher\Loading\WorkflowFunctionRefValidator.cs   （O-3 清单）
      Projects\Plug\Agent\src\Agent.Modules\{Attribution,Provision}\*OpsFunctions.cs   （O-2 单步体）
      Projects\Plug\Agent\src\Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs（O-5 静默基线）
      Projects\Plug\Agent\doc\Agent编排载体分工表.md                                   （O-6 载体分工 + 豁免登记）

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
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# ---------------- 基线（只减不增；变更须给出理由并同步本表 + 载体分工表 §3） ----------------
$workflowsDir   = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
$specCatalog    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs'
$fnNamesFile    = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\WorkflowFunction\WorkflowFunctionNames.cs'
$paradigmsFile  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\LoopParadigms.cs'
$validatorFile  = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Loading\WorkflowFunctionRefValidator.cs'
$pipelineFile   = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Orchestration\SessionWorkflow\ConversationCentralPipeline.cs'
$carrierDoc     = Join-Path $Root 'Projects\Plug\Agent\doc\Agent编排载体分工表.md'
$cliExe         = Join-Path $Root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
$cliProj        = Join-Path $Root 'Frame\Usharp\src\Usharp.Cli\Usharp.Cli.csproj'

# R1/R2/R5 新增声明面（闸A 编译对象）
$newDeclarationFiles = @('attribution.ushar', 'task-provision.ushar', 'stateless-answer.ushar')

# fn 豁免白名单（编排性阶段名：非引擎注册函数，纳入校验语义不成立）
$FnExemptNames = @('initialize', 'load', 'process', 'save', 'complete')
# 声明文件豁免清单（不进 `WorkflowFunctionRefValidator.WorkflowFiles`；理由见载体分工表 §3）
$ValidatorExemptFiles = @('session-lifecycle.ushar', 'stateless-answer.ushar')

# 声明面基线（防声明被删而门禁恒绿；变更须同步本表）
$DeclaredFlowCountBaseline = 11
$FnConstCountBaseline      = 25
# O-5 静默降级基线（只减不增）：ConversationCentralPipeline 的 `ctx.Spec is null) return;` 现存 4 处
# （登记豁免：会话腿 spec 解析未识别 ⇒ 诊断 runner 承担，见载体分工表 §3 E2；新增即违规）
$SilentSkipBaseline = 4
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

# ---------------- 取数：C# 常量 ----------------
function Get-CSharpConstMap {
    param([string]$Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), 'public const string (?<n>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"(?<v>[^"]+)"')) {
        $map[$m.Groups['n'].Value] = $m.Groups['v'].Value
    }
    return $map
}

function Get-DeclaredFlowIds {
    param([string]$Path)
    $map = @{}
    if (-not (Test-Path -LiteralPath $Path)) { return $map }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), '\["(?<b>[a-z0-9\-]+)"\]\s*=\s*"(?<f>[A-Za-z0-9]+)"')) {
        $map[$m.Groups['b'].Value] = $m.Groups['f'].Value
    }
    return $map
}

function Get-WorkflowFiles {
    param([string]$Path, [hashtable]$SpecConsts)
    $files = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $Path)) { return $files }
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $Path -Raw), 'WorkflowSpecFiles\.(?<n>[A-Za-z_][A-Za-z0-9_]*)')) {
        $name = $m.Groups['n'].Value
        if ($SpecConsts.ContainsKey($name)) { $files.Add($SpecConsts[$name]) }
    }
    return $files
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
foreach ($dir in @('Projects\Plug\Agent\src\Agent.Modules\Attribution', 'Projects\Plug\Agent\src\Agent.Modules\Provision')) {
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
    # 正对照：须含原语调用（否则「零关键词」可能是空文件 / 未接线）
    if ($raw -notmatch 'CreateAsync|AttachTaskAsync|EnsureTaskNodeLinkAsync') {
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

# ---------------- E8 正对照 ----------------
if ($flowIds.Count -lt $DeclaredFlowCountBaseline) {
    $violations.Add("E8 正对照失效：声明流 $($flowIds.Count) < 基线 $DeclaredFlowCountBaseline（声明被删？）")
}
if ($fnConstValues.Count -lt $FnConstCountBaseline) {
    $violations.Add("E8 正对照失效：fn 常量 $($fnConstValues.Count) < 基线 $FnConstCountBaseline（常量文件被清空？）")
}

# ---------------- E9 负对照（同一判据对合成违规必须命中） ----------------
$probePath = Join-Path ([System.IO.Path]::GetTempPath()) ("aof-probe-" + [guid]::NewGuid().ToString('n') + ".ushar")
try {
    Set-Content -LiteralPath $probePath -Value "[Node] bogus: fn=__no_such_fn__ | inports=x" -Encoding UTF8
    $probeBad = Test-FnNamesDeclared -UsharPath $probePath -FnConstValues $fnConstValues
    if (@($probeBad).Count -eq 0) {
        $violations.Add("E9 负对照失效：E3 判据对合成违规未命中（判据恒真？）")
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
}

if ($ok) {
    Write-Host ""
    Write-Host "  [PASS] 编排归流守卫：0 违规（E1–E9）" -ForegroundColor Green
    Write-Host ""
    exit 0
}

Write-Host ""
Write-Host "  违规明细（$($violations.Count) 项）：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Yellow }
Write-Host ""
if ($Check) { exit 1 }
exit 0
