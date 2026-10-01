#requires -Version 5.1
<#
.SYNOPSIS
    电路语义守卫（CircuitSemantics）—— 防止在 CircuitFramework 内新增平行引用抽象、要求唯一宿主接线。

.DESCRIPTION
    CS1 唯一宿主接线：CircuitFramework.csproj 必须引用 UniversalWorkflowFramework（引用宿主）。
    CS2 禁新增平行抽象（负对照）：CircuitFramework 源码内禁止定义 `*ReferenceManager / *ReferenceService /
        *RelationshipService / *GraphService` 之类接口/类（唯一宿主铁律）。
    CS3 引用符号在册（正对照）：`ReferenceRelation` 五种引用语义（Owns/Calls/Depends/Provides/Consumes）
        在 ReferenceSymbol.cs 中齐备。
    CS4 轮骨架唯一接线（正对照）：Agent WorkflowRunner 默认轮后端必须接线 RoundCircuitBackend（函数流执行
        由电路承载）；Nodes\Circuit 内禁定义平行引用抽象（负对照，引用唯一宿主铁律外延）。
    CS5 装配域唯一接线（编排机制 3→1 / 配方 DSL 3→1 / 段壳样板 → 0 的固化）：
        CS5-a 禁自建装配管线（Agent.Launcher\Composition 内 CreateStagePipeline = 0；编排唯一 = AssemblyPhaseRunner）；
        CS5-b 禁手工配方（Agent.Launcher 内 new CircuitStepSpec(/new CircuitFlowSpec( = 0；配方唯一 = OrderedRecipe/RecipeBuilder）；
        CS5-c 禁直调执行外壳（Agent.Launcher 内 CircuitAssemblyRunner.RunAsync = 0；绑定/驱动唯一 = RecipeAssembly）；
        CS5-d 正对照（RecipeAssembly< 使用点 ≥ 1；0 = 扫描面失效，非「零违规」）。
    CS6 Agent.Core 编译入口唯一（负对照）：Nodes\Circuit（含旧 Circuit\）内禁自行重排「配方校验 → 投影 → 编译」
        （唯一入口 = 框架 CircuitCompiler.TryCompile / Compile(spec)）。
    CS7 Agent.Core 禁桩件体（负对照）：接入面内禁 模拟/stub/NotImplemented/TODO（演示件冒充真实接线）。
    CS8 Agent.Core 缓存键内容指纹（正对照）：RoundFlowCircuitRegistry 须含 Fingerprint（同名不同内容不得复用陈旧电路）。
    CS9 Agent.Core 编排动作分派配方化（负对照 + 正对照）：TaskOrchestrator 内禁手写 op 分派
        （switch(operation.Op) = 0 且「执行体无对应分支」漂移兜底 = 0；Kind 解析唯一来源 = 框架 CircuitNodeResolver）；
        CS9-d 正对照 = 配方驱动在册（CircuitAssemblyRunner.RunAsync ≥ 1 且 new RecipeBuilder( ≥ 1）。
    CS10 区域子计划编译入口唯一（负对照 + 正对照）：编译器（CircuitCompiler.cs）之外禁重排「配方域校验 → 电路域校验」
        （RecipeGraphValidator.Validate 与 CircuitValidator.Validate 同文件共存 = 0）；
        CS10-d 正对照 = 区域执行面在册（CompiledRecipeRegistry 经 CircuitCompiler.TryCompile 编译、
        RegionNodeBase 经 CompiledRecipeRegistry.GetOrCompile 取子计划）。
        CS10-e 区域镜像单一来源（负对照）：投影产出的 LoopSymbol.Body 只是符号域镜像，
        除投影处（CircuitRecipeProjector.cs）与符号定义处（Symbols\RecursionSymbols.cs）外引用 LoopSymbol = 0
        （被执行面引用即出现第二执行真源，与注册表指纹键分叉）。
    CS12 轮间链单驱动（负对照）：Agent.Core\Loops 内禁平行路由表 RouteTable<AgentRunState> 与平行脊柱驱动器
        SpinalPipeline = 0（run 主路径唯一 = RoundLoopDriver.RunViaRegionAsync 区域电路）。
    CS13 区域化轮循环在册（正对照）：区域节点（RegionNodeBase.For）/ 条件迭代计划
        （RegionIterationPlan.WhileTrue）/ 区域入口（RoundLoopDriver.RunViaRegionAsync）须齐备。
    CS15 run 主干在册（正对照，P1/T2 配方化）：Agent.Core\Loops\Circuit\AgentRunTrunkCircuit.cs 存在且以
        OrderedRecipe< 声明 agent.run.trunk 顺序链（load_state…finalize_run）；缺失 = 扫描面失效（FAIL，非「零违规」）。
    CS16 禁命令式主干（负对照）：AgentRunner 内禁内联主干序列（using var ports = BuildRunPorts( = 0）；
        run 主干须经 AgentRunTrunkCircuit 配方驱动（与 CS12 单驱动同族）。
    CS17 受控第二点（负对照）：RunSingleRootAsync（兼容入口）的引用文件须 ⊆ 已知集
        （RoundLoopDriver 定义 + RuleTaskExecutor / StatelessAnswerLoop 两消费方）；新增平行消费方须先过迁移决议。
    CS18 轮内链声明面在册（正对照）：RoundFlowSpecMapper.EnforceDeclaredEdgeCoverage（未声明真依赖判红）
        与 RoundSpecLoader.ApplyDeclaredDependencies（[Matrix] 反解）须齐备（R1/R3 不回退）。
    CS23 递归执行化在册（正对照）：RegionNodeBase 按 RegionIterationKind.Recursion 驱动（do-while 到基例即停）；
        CircuitExecutionCapabilities 含 Recursion/BaseCase；CircuitRecipeProjector 产出 RecursionSymbol 镜像
        （缺失 = 「表内有、引擎无」假支持，与 CS10-d/CS13 同族）。
    CS24 分支/分派执行化在册（正对照，R1）：CircuitExecutionCapabilities 含 Branch/Switch；
        CircuitRuntimeIndex 具 RouteMatch 路由表；CircuitRecipeProjector 产出 BranchSymbol。
    CS25 调用执行化在册（正对照，R2）：CircuitExecutionCapabilities 含 Call；Execution\SubCircuitNode 经
        CircuitEngine.ExecuteAsync 驱动子电路；CFG-002 环检测仍在册（调用边不豁免成环禁入）。
    CS27 全流程图投影/渲染在册（正对照，R4）：Analysis\ControlFlowGraph 具 FlowEdgeKind 全边；
        Analysis\CircuitFlowRenderer 具 ToMermaid（DOT/Mermaid/JSON 声明式渲染）。

    模式：默认报告模式；-Check 校验模式（违归 → 退出码 1）。纯结构读检查，无外部基线依赖。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：违归 → 退出码 1。
.PARAMETER ShowDetail
    输出逐命中点明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  电路语义守卫（唯一宿主接线 / 禁平行引用抽象 / 符号在册 / 轮骨架唯一接线 / 装配域唯一接线 / 接入面编译入口唯一）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$cfDir = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\CircuitFramework'
if (-not (Test-Path $cfDir))
{
    Write-Host "[FAIL] 未找到 CircuitFramework 工程目录：$cfDir" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------- CS1 唯一宿主接线 ----------
$csproj = Join-Path $cfDir 'CircuitFramework.csproj'
$csprojText = Get-Content -LiteralPath $csproj -Raw -Encoding UTF8
$cs1Ok = $csprojText -match 'UniversalWorkflowFramework'
Write-Host ("  CS1 唯一宿主接线（引用 UniversalWorkflowFramework）：{0}" -f $(if ($cs1Ok) { 'OK' } else { '缺失' })) -ForegroundColor Gray
if (-not $cs1Ok)
{
    $violations.Add("[CS1] CircuitFramework.csproj 未引用 UniversalWorkflowFramework（引用唯一宿主未接线）")
}

# ---------- CS2 禁新增平行引用抽象（负对照） ----------
$allCs = @(Get-ChildItem -Path $cfDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
$paraPattern = '(?i)(interface|class)\s+(I?[A-Za-z]*(ReferenceManager|ReferenceService|RelationshipService|GraphService))\b'
$cs2Hits = @($allCs | ForEach-Object {
    $m = Select-String -Path $_.FullName -Pattern $paraPattern -AllMatches -ErrorAction SilentlyContinue
    if ($m) { $m }
    } | Select-Object -Unique Path)
$cs2Actual = $cs2Hits.Count
Write-Host ("  CS2 平行引用抽象定义（负对照）：{0} 处（须为 0）" -f $cs2Actual) -ForegroundColor Gray
if ($cs2Actual -gt 0)
{
    $violations.Add("[CS2] 在 CircuitFramework 内定义平行引用抽象 $cs2Actual 处（唯一宿主铁律：关系必须经 UniversalReferenceProjector→ReferenceSymbol，禁止 I*ReferenceManager 等平行门面）")
}
if ($ShowDetail) { foreach ($h in $cs2Hits) { Write-Host ("    {0}" -f ($h -replace [regex]::Escape($Root + '\'), '')) -ForegroundColor DarkGray } }

# ---------- CS3 引用符号在册（正对照） ----------
$refSym = Join-Path $cfDir 'Symbols\ReferenceSymbol.cs'
if (-not (Test-Path $refSym))
{
    $violations.Add("[CS3] 缺失 Symbols\ReferenceSymbol.cs（引用符号未在册）")
}
else
{
    $refText = Get-Content -LiteralPath $refSym -Raw -Encoding UTF8
    $expectedRels = 'ReferenceRelation.Owns','ReferenceRelation.Calls','ReferenceRelation.Depends',
                    'ReferenceRelation.Provides','ReferenceRelation.Consumes'
    foreach ($r in $expectedRels)
    {
        if ($refText -match [regex]::Escape($r)) { continue }
        $violations.Add("[CS3] ReferenceSymbol 缺失引用关系语义定义：$r")
    }
    if ($violations.Count -eq 0) { Write-Host "  CS3 引用符号在册（5 种引用关系）：OK" -ForegroundColor Gray }
}

# ---------- CS4 轮骨架唯一接线（正对照）+ Nodes\Circuit 禁平行抽象（负对照） ----------
$agentNodesCircuit = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\Circuit'
$wfRunner = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\WorkflowRunner.cs'
if ((Test-Path $agentNodesCircuit) -and (Test-Path $wfRunner))
{
    # 正对照：WorkflowRunner 默认轮后端必须接线 RoundCircuitBackend（电路为唯一执行后端）
    $wfText = Get-Content -LiteralPath $wfRunner -Raw -Encoding UTF8
    $cs4aOk = $wfText -match 'new RoundCircuitBackend'
    Write-Host ("  CS4-a 轮骨架唯一接线（WorkflowRunner 默认后端 = RoundCircuitBackend）：{0}" -f $(if ($cs4aOk) { 'OK' } else { '缺失' })) -ForegroundColor Gray
    if (-not $cs4aOk) { $violations.Add('[CS4-a] WorkflowRunner 默认轮后端未接线 RoundCircuitBackend（函数流执行须由电路承载）') }

    # 负对照：Nodes\Circuit 内禁定义平行引用抽象（引用唯一宿主铁律外延）
    $circuitCs = @(Get-ChildItem -Path $agentNodesCircuit -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    $cs4bHits = @($circuitCs | ForEach-Object {
        $m = Select-String -Path $_.FullName -Pattern $paraPattern -AllMatches -ErrorAction SilentlyContinue
        if ($m) { $m }
        } | Select-Object -Unique Path)
    $cs4bActual = $cs4bHits.Count
    Write-Host ("  CS4-b Nodes\Circuit 平行引用抽象（负对照）：{0} 处（须为 0）" -f $cs4bActual) -ForegroundColor Gray
    if ($cs4bActual -gt 0)
    {
        $violations.Add("[CS4-b] Agent.Core\Nodes\Circuit 内定义平行引用抽象 $cs4bActual 处（引用关系统一宿主 IUniversalReferenceManager）")
    }
    if ($ShowDetail) { foreach ($h in $cs4bHits) { Write-Host ("    {0}" -f ($h -replace [regex]::Escape($Root + '\'), '')) -ForegroundColor DarkGray } }
}
else
{
    Write-Host "  CS4 路径缺失（Agent.Core\Nodes\Circuit / WorkflowRunner.cs 未找到）：跳过" -ForegroundColor DarkGray
}

# ---------- CS5 装配域唯一接线（编排机制 3→1 / 配方 DSL 3→1 / 段壳样板 → 0 的固化） ----------
$launcherDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher'
if (-not (Test-Path $launcherDir))
{
    Write-Host "  CS5 路径缺失（Agent.Launcher 未找到）：跳过" -ForegroundColor DarkGray
}
else
{
    $launcherCs = @(Get-ChildItem -Path $launcherDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    $compositionCs = @($launcherCs | Where-Object { $_.FullName -match '\\Composition\\' })

    # CS5-a 负对照：编排唯一 = AssemblyPhaseRunner（禁再起 Modules.Pipeline 段管线）
    $cs5a = @($compositionCs | Select-String -Pattern 'CreateStagePipeline' -ErrorAction SilentlyContinue)
    Write-Host ("  CS5-a 装配编排唯一（Composition 内 CreateStagePipeline）：{0} 处（须为 0）" -f $cs5a.Count) -ForegroundColor Gray
    if ($cs5a.Count -gt 0)
    {
        $violations.Add("[CS5-a] Agent.Launcher\Composition 内出现 Modules.Pipeline 段管线 $($cs5a.Count) 处（装配编排唯一宿主 = AssemblyPhaseRunner）")
    }

    # CS5-b 负对照：配方声明唯一经框架 DSL（禁手写 CircuitStepSpec/CircuitFlowSpec 数组绕过 RecipeChainCore）
    $cs5b = @($launcherCs | Select-String -Pattern 'new CircuitStepSpec\(|new CircuitFlowSpec\(' -ErrorAction SilentlyContinue)
    Write-Host ("  CS5-b 配方声明统一（Launcher 内手工 CircuitStepSpec/CircuitFlowSpec 构点）：{0} 处（须为 0）" -f $cs5b.Count) -ForegroundColor Gray
    if ($cs5b.Count -gt 0)
    {
        $violations.Add("[CS5-b] Agent.Launcher 内手工构造配方步骤/边 $($cs5b.Count) 处（配方须经 OrderedRecipe / RecipeBuilder 声明）")
    }

    # CS5-c 负对照：执行外壳唯一 = 框架 CircuitAssemblyRunner（禁消费方直调；绑定/驱动经 RecipeAssembly）
    $cs5c = @($launcherCs | Select-String -Pattern 'CircuitAssemblyRunner\.RunAsync' -ErrorAction SilentlyContinue)
    Write-Host ("  CS5-c 执行外壳唯一（Launcher 内直调 CircuitAssemblyRunner.RunAsync）：{0} 处（须为 0）" -f $cs5c.Count) -ForegroundColor Gray
    if ($cs5c.Count -gt 0)
    {
        $violations.Add("[CS5-c] Agent.Launcher 内直调执行外壳 $($cs5c.Count) 处（绑定/驱动须经框架装配壳 RecipeAssembly）")
    }

    # CS5-d 正对照：装配壳在册（0 = 扫描面失效，非「零违规」）
    $cs5d = @($launcherCs | Select-String -Pattern 'RecipeAssembly<' -ErrorAction SilentlyContinue)
    Write-Host ("  CS5-d 装配壳在册（正对照 RecipeAssembly< 使用点）：{0} 处（须 ≥ 1）" -f $cs5d.Count) -ForegroundColor Gray
    if ($cs5d.Count -lt 1)
    {
        $violations.Add("[CS5-d] Agent.Launcher 内未发现框架装配壳 RecipeAssembly 使用点（扫描面失效或接线回退）")
    }

    if ($ShowDetail)
    {
        foreach ($h in @($cs5a + $cs5b + $cs5c))
        {
            Write-Host ("    {0}:{1}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber) -ForegroundColor DarkGray
        }
    }
}

# ---------- CS6/CS7/CS8 Agent.Core 电路接入面（接入收敛 / 禁桩件 / 缓存键正对照） ----------
$agentCoreCircuitDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\Circuit'
$agentCoreLegacyCircuitDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Circuit'
if (Test-Path $agentCoreCircuitDir)
{
    $adoptionCs = @(Get-ChildItem -Path $agentCoreCircuitDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    if (Test-Path $agentCoreLegacyCircuitDir)
    {
        $adoptionCs += @(Get-ChildItem -Path $agentCoreLegacyCircuitDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    }

    # CS6 负对照：禁自行重排「配方校验 → 投影 → 编译」（唯一编译入口 = 框架 CircuitCompiler.TryCompile / Compile(spec)）
    $cs6 = @($adoptionCs | Select-String -Pattern 'RecipeGraphValidator\.Validate\(|CircuitRecipeProjector\.Project\(' -ErrorAction SilentlyContinue)
    Write-Host ("  CS6 编译入口唯一（接入面内重排 校验/投影）：{0} 处（须为 0）" -f $cs6.Count) -ForegroundColor Gray
    if ($cs6.Count -gt 0)
    {
        $violations.Add("[CS6] Agent.Core 电路接入面内自行重排配方校验/投影 $($cs6.Count) 处（唯一编译入口 = 框架 CircuitCompiler.TryCompile / Compile(spec)）")
    }

    # CS7 负对照：禁桩件体（演示/占位实现冒充真实接线）
    $cs7 = @($adoptionCs | Select-String -Pattern '模拟|自足 stub|NotImplementedException|TODO' -ErrorAction SilentlyContinue)
    Write-Host ("  CS7 禁桩件体（接入面内 模拟/stub/NotImplemented/TODO）：{0} 处（须为 0）" -f $cs7.Count) -ForegroundColor Gray
    if ($cs7.Count -gt 0)
    {
        $violations.Add("[CS7] Agent.Core 电路接入面内出现桩件/占位实现 $($cs7.Count) 处（电路接入必须接真实执行体）")
    }

    # CS8 正对照：缓存键须为内容指纹（防陈旧电路复用 + 防扫描面失效假绿）
    $registryFile = Join-Path $agentCoreCircuitDir 'RoundFlowCircuitRegistry.cs'
    $cs8Ok = (Test-Path $registryFile) -and ((Get-Content -LiteralPath $registryFile -Raw -Encoding UTF8) -match 'Fingerprint')
    Write-Host ("  CS8 缓存键内容指纹（正对照 RoundFlowCircuitRegistry.Fingerprint）：{0}" -f $(if ($cs8Ok) { 'OK' } else { '缺失' })) -ForegroundColor Gray
    if (-not $cs8Ok)
    {
        $violations.Add('[CS8] RoundFlowCircuitRegistry 缓存键未见内容指纹（Fingerprint）：同名不同内容会复用陈旧电路')
    }

    if ($ShowDetail)
    {
        foreach ($h in @($cs6 + $cs7))
        {
            Write-Host ("    {0}:{1}" -f ($h.Path -replace [regex]::Escape($Root + '\'), ''), $h.LineNumber) -ForegroundColor DarkGray
        }
    }
}
else
{
    Write-Host "  CS6/CS7/CS8 路径缺失（Agent.Core\Nodes\Circuit 未找到）：跳过" -ForegroundColor DarkGray
}

# ---------- CS9 编排动作分派配方化（负对照：禁手写 op 分派；正对照：配方驱动在册） ----------
$orchestratorFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\TaskPool\TaskOrchestrator.cs'
if (Test-Path $orchestratorFile)
{
    # 载体 = 结构事实（代码语句）而非注释散文：先剥 `//` 注释行，
    # 防「注释里提到 switch 即判红」的假阳性（判据载体须为代码形态，权威：SEP.JudgmentCarrierStructural）。
    $orchestratorCode = ((Get-Content -LiteralPath $orchestratorFile -Encoding UTF8) |
        Where-Object { $_ -notmatch '^\s*//' }) -join "`n"

    # 负对照：手写分派（switch(operation.Op)）与「词表已声明但执行体缺分支」的漂移兜底
    $cs9Dispatch = ([regex]::Matches($orchestratorCode, 'switch\s*\(\s*operation\.Op\s*\)')).Count
    $cs9Drift = ([regex]::Matches($orchestratorCode, '但执行体无对应分支')).Count
    Write-Host ("  CS9 编排动作分派配方化（负对照 switch(operation.Op)={0} / 漂移兜底={1}）：须均为 0" -f $cs9Dispatch, $cs9Drift) -ForegroundColor Gray
    if (($cs9Dispatch + $cs9Drift) -gt 0)
    {
        $violations.Add("[CS9] TaskOrchestrator 仍存在手写 op 分派 / 漂移兜底 $($cs9Dispatch + $cs9Drift) 处（编排须经电路配方 + 框架单源 Kind 解析）")
    }

    # 正对照：配方驱动在册（0 = 扫描面失效，非「零违规」）
    $cs9Recipe = ([regex]::Matches($orchestratorCode, 'CircuitAssemblyRunner\.RunAsync')).Count
    $cs9Builder = ([regex]::Matches($orchestratorCode, 'new RecipeBuilder\(')).Count
    Write-Host ("  CS9-d 配方驱动在册（正对照 RunAsync={0} / RecipeBuilder={1}）：须均 ≥ 1" -f $cs9Recipe, $cs9Builder) -ForegroundColor Gray
    if ($cs9Recipe -lt 1 -or $cs9Builder -lt 1)
    {
        $violations.Add("[CS9-d] TaskOrchestrator 未见配方驱动（CircuitAssemblyRunner.RunAsync / RecipeBuilder）：扫描面失效或编排回退为手写分派")
    }
}
else
{
    Write-Host "  CS9 路径缺失（Agent.Core\TaskPool\TaskOrchestrator.cs 未找到）：跳过" -ForegroundColor DarkGray
}

# ---------- CS10 区域子计划编译入口唯一（负对照：禁重排编译器步骤；正对照：区域执行面在册） ----------
$cfCompilerFile = Join-Path $cfDir 'Compiler\CircuitCompiler.cs'
$cs10Outside = @($allCs | Where-Object { $_.FullName -ne $cfCompilerFile } | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    if ($text -match 'RecipeGraphValidator\.Validate' -and $text -match 'CircuitValidator\.Validate') { $_ }
})
Write-Host ("  CS10 区域子计划编译入口唯一（负对照：编译器外重排配方域/电路域校验的文件数）：{0} 处（须为 0）" -f $cs10Outside.Count) -ForegroundColor Gray
if ($cs10Outside.Count -gt 0)
{
    $violations.Add("[CS10] CircuitCompiler 之外重排「配方域校验 + 电路域校验」$($cs10Outside.Count) 处（子计划须经 CircuitCompiler.TryCompile 唯一入口）")
}

$registryFile = Join-Path $cfDir 'Execution\CompiledRecipeRegistry.cs'
$regionNodeFile = Join-Path $cfDir 'Execution\RegionNodeBase.cs'
$cs10Registry = (Test-Path $registryFile) -and
    ((Get-Content -LiteralPath $registryFile -Raw -Encoding UTF8) -match 'CircuitCompiler\.TryCompile')
$cs10Use = (Test-Path $regionNodeFile) -and
    ((Get-Content -LiteralPath $regionNodeFile -Raw -Encoding UTF8) -match 'CompiledRecipeRegistry\.GetOrCompile')
Write-Host ("  CS10-d 区域执行面在册（正对照：注册表经唯一入口={0} / 区域节点用注册表={1}）：须均为 True" -f $cs10Registry, $cs10Use) -ForegroundColor Gray
if (-not $cs10Registry -or -not $cs10Use)
{
    $violations.Add('[CS10-d] 区域子计划注册表/区域节点缺失或未走唯一编译入口（扫描面失效或回退为裸跑）')
}

# ---------- CS10-e 区域镜像单一来源（负对照：LoopSymbol 不得被执行面消费） ----------
# 区域执行真源 = CompiledRecipeRegistry（内容指纹 → 子计划）；投影产出的 LoopSymbol.Body 只是符号域镜像。
# 被执行面引用即出现「第二真源」（镜像与注册表可能分叉）⇒ 除投影处与符号定义处外命中须为 0。
$mirrorOwners = @(
    (Join-Path $cfDir 'Projection\CircuitRecipeProjector.cs'),
    (Join-Path $cfDir 'Symbols\RecursionSymbols.cs'))
$cs10Mirror = @($allCs | Where-Object { $mirrorOwners -notcontains $_.FullName } | ForEach-Object {
    if ((Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8) -match 'LoopSymbol') { $_ }
})
Write-Host ("  CS10-e 区域镜像单一来源（负对照：投影/符号定义处之外引用 LoopSymbol 的文件数）：{0} 处（须为 0）" -f $cs10Mirror.Count) -ForegroundColor Gray
if ($cs10Mirror.Count -gt 0)
{
    $violations.Add("[CS10-e] 区域 Loop 镜像被执行面引用 $($cs10Mirror.Count) 处（Body 不得成为第二执行真源；执行真源 = CompiledRecipeRegistry）")
}
if ($ShowDetail) { foreach ($h in $cs10Mirror) { Write-Host ("    {0}" -f ($h.FullName -replace [regex]::Escape($Root + '\'), '')) -ForegroundColor DarkGray } }

# ---------- CS23 递归执行化在册（正对照：Recursion/BaseCase 有执行面） ----------
# F3：RegionNodeBase 按 RegionIterationKind.Recursion 驱动（do-while 到基例即停）；能力表含 Recursion/BaseCase；
# 投影产出 RecursionSymbol 镜像。缺失 = 「表内有、引擎无」假支持（与 CS10-d/CS13 同族正对照，非负对照）。
$cfCapFile = Join-Path $cfDir 'Compiler\CircuitExecutionCapabilities.cs'
$cfProjectorFile = Join-Path $cfDir 'Projection\CircuitRecipeProjector.cs'
$cs23Exec = (Test-Path $regionNodeFile) -and
    ((Get-Content -LiteralPath $regionNodeFile -Raw -Encoding UTF8) -match 'RegionIterationKind\.Recursion')
$cs23Cap = (Test-Path $cfCapFile) -and
    ((Get-Content -LiteralPath $cfCapFile -Raw -Encoding UTF8) -match 'CircuitSymbolKind\.Recursion') -and
    ((Get-Content -LiteralPath $cfCapFile -Raw -Encoding UTF8) -match 'CircuitSymbolKind\.BaseCase')
$cs23Project = (Test-Path $cfProjectorFile) -and
    ((Get-Content -LiteralPath $cfProjectorFile -Raw -Encoding UTF8) -match 'RecursionSymbol')
Write-Host ("  CS23 递归执行化在册（正对照：执行面={0} / 能力表={1} / 投影={2}）：须均为 True" -f $cs23Exec, $cs23Cap, $cs23Project) -ForegroundColor Gray
if (-not ($cs23Exec -and $cs23Cap -and $cs23Project))
{
    $violations.Add('[CS23] 递归执行化在册缺失（RegionNodeBase / CircuitExecutionCapabilities / CircuitRecipeProjector 任一处未接线：能力表与执行面脱节）')
}
# ---------- CS24 分支/分派执行化在册（正对照：Branch/Switch 有执行面） ----------
# R1：声明 → BranchSymbol/SwitchSymbol；执行面 = CircuitRuntimeIndex 路由表（RouteMatch）+ 引擎后继门控。
# 缺失 = 「表内有、引擎无」假支持（与 CS23 同族正对照）。
$cs24Cap = (Test-Path $cfCapFile) -and
    ((Get-Content -LiteralPath $cfCapFile -Raw -Encoding UTF8) -match 'CircuitSymbolKind\.Branch') -and
    ((Get-Content -LiteralPath $cfCapFile -Raw -Encoding UTF8) -match 'CircuitSymbolKind\.Switch')
$cfRuntimeFile = Join-Path $cfDir 'Compiler\CircuitRuntimeIndex.cs'
$cs24Route = (Test-Path $cfRuntimeFile) -and
    ((Get-Content -LiteralPath $cfRuntimeFile -Raw -Encoding UTF8) -match 'RouteMatch')
$cs24Project = (Test-Path $cfProjectorFile) -and
    ((Get-Content -LiteralPath $cfProjectorFile -Raw -Encoding UTF8) -match 'BranchSymbol')
Write-Host ("  CS24 分支/分派执行化在册（正对照：能力表={0} / 路由表={1} / 投影={2}）：须均为 True" -f $cs24Cap, $cs24Route, $cs24Project) -ForegroundColor Gray
if (-not ($cs24Cap -and $cs24Route -and $cs24Project))
{
    $violations.Add('[CS24] 分支/分派执行面缺失（CircuitExecutionCapabilities / CircuitRuntimeIndex / CircuitRecipeProjector 任一处未接线）')
}

# ---------- CS25 调用执行化在册（正对照：Call 有执行面 + CFG-002 环检测仍红） ----------
# R2：声明 → CallSymbol；执行面 = Execution.SubCircuitNode；CFG-002（Wire 成环禁入）不变（调用边不豁免）。
$cfSubNodeFile = Join-Path $cfDir 'Execution\SubCircuitNode.cs'
$cfValidatorFile = Join-Path $cfDir 'Compiler\CircuitValidator.cs'
$cs25Cap = (Test-Path $cfCapFile) -and
    ((Get-Content -LiteralPath $cfCapFile -Raw -Encoding UTF8) -match 'CircuitSymbolKind\.Call')
$cs25Node = (Test-Path $cfSubNodeFile) -and
    ((Get-Content -LiteralPath $cfSubNodeFile -Raw -Encoding UTF8) -match 'CircuitEngine\.ExecuteAsync')
$cs25Cfg002 = (Test-Path $cfValidatorFile) -and
    ((Get-Content -LiteralPath $cfValidatorFile -Raw -Encoding UTF8) -match 'CFG-002')
Write-Host ("  CS25 调用执行化在册（正对照：能力表={0} / 子电路节点={1} / CFG-002 在册={2}）：须均为 True" -f $cs25Cap, $cs25Node, $cs25Cfg002) -ForegroundColor Gray
if (-not ($cs25Cap -and $cs25Node -and $cs25Cfg002))
{
    $violations.Add('[CS25] 调用执行面缺失（CircuitExecutionCapabilities / SubCircuitNode / CFG-002 任一处未接线）')
}

# ---------- CS27 全流程图投影/渲染在册（正对照：ControlFlowGraph + CircuitFlowRenderer） ----------
# R4：全流程图（分支/区域/调用全边）+ 声明式渲染（DOT/Mermaid/JSON）。
$cfFlowGraphFile = Join-Path $cfDir 'Analysis\ControlFlowGraph.cs'
$cfRendererFile = Join-Path $cfDir 'Analysis\CircuitFlowRenderer.cs'
$cs27Graph = (Test-Path $cfFlowGraphFile) -and
    ((Get-Content -LiteralPath $cfFlowGraphFile -Raw -Encoding UTF8) -match 'FlowEdgeKind')
$cs27Render = (Test-Path $cfRendererFile) -and
    ((Get-Content -LiteralPath $cfRendererFile -Raw -Encoding UTF8) -match 'ToMermaid')
Write-Host ("  CS27 全流程图/渲染在册（正对照：ControlFlowGraph={0} / Renderer={1}）：须均为 True" -f $cs27Graph, $cs27Render) -ForegroundColor Gray
if (-not ($cs27Graph -and $cs27Render))
{
    $violations.Add('[CS27] 全流程图投影/渲染缺失（ControlFlowGraph / CircuitFlowRenderer 未在册）')
}



# ---------- CS12 轮间链单驱动（负对照：Loops 内禁平行路由表）+ CS13 区域化轮循环在册（正对照） ----------
$agentLoopsDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Loops'
if (Test-Path $agentLoopsDir)
{
    $loopsCs = @(Get-ChildItem -Path $agentLoopsDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)

    $cs12 = @($loopsCs | Select-String -Pattern 'RouteTable<AgentRunState>|new SpinalPipeline<' -ErrorAction SilentlyContinue)
    Write-Host ("  CS12 轮间链单驱动（负对照：Loops 内 RouteTable<AgentRunState>/SpinalPipeline 引用）：{0} 处（须为 0）" -f $cs12.Count) -ForegroundColor Gray
    if ($cs12.Count -gt 0)
    {
        $violations.Add("[CS12] 轮间链出现平行装配（路由表/脊柱驱动器）$($cs12.Count) 处（run 主路径须由区域电路单驱动：RoundLoopDriver.RunViaRegionAsync）")
    }

    $regionCircuitFile = Join-Path $agentLoopsDir 'Circuit\AgentRunRegionCircuit.cs'
    $runCircuitFile = Join-Path $agentLoopsDir 'Circuit\AgentRunCircuit.cs'
    $loopDriverFile = Join-Path $agentLoopsDir 'RoundLoopDriver.cs'
    $cs13Region = (Test-Path $regionCircuitFile) -and
        ((Get-Content -LiteralPath $regionCircuitFile -Raw -Encoding UTF8) -match 'RegionNodeBase\.For')
    $cs13Plan = (Test-Path $runCircuitFile) -and
        ((Get-Content -LiteralPath $runCircuitFile -Raw -Encoding UTF8) -match 'RegionIterationPlan\.WhileTrue')
    $cs13Entry = (Test-Path $loopDriverFile) -and
        ((Get-Content -LiteralPath $loopDriverFile -Raw -Encoding UTF8) -match 'RunViaRegionAsync')
    Write-Host ("  CS13 区域化轮循环在册（正对照：区域节点={0} / 条件迭代计划={1} / 区域入口={2}）：须均为 True" -f $cs13Region, $cs13Plan, $cs13Entry) -ForegroundColor Gray
    if (-not ($cs13Region -and $cs13Plan -and $cs13Entry))
    {
        $violations.Add('[CS13] 区域化轮循环执行面缺失（区域节点/条件迭代计划/区域入口未在册；轮间链未进电路）')
    }

    # ---------- CS15 run 主干在册（正对照，P1/T2 配方化）+ CS16 禁命令式主干（负对照） ----------
    $trunkCircuitFile = Join-Path $agentLoopsDir 'Circuit\AgentRunTrunkCircuit.cs'
    $agentRunnerFile = Join-Path $agentLoopsDir 'AgentRunner.cs'

    # CS15-d 正对照：扫描面非空（文件存在 + OrderedRecipe< 使用 + 主干步骤声明）；缺失 = 扫描面失效（FAIL，非「零违规」）。
    $trunkExists = Test-Path $trunkCircuitFile
    $trunkText = if ($trunkExists) { Get-Content -LiteralPath $trunkCircuitFile -Raw -Encoding UTF8 } else { '' }
    $cs15Recipe = $trunkExists -and ($trunkText -match 'OrderedRecipe<')
    $cs15Trunk = $cs15Recipe -and ($trunkText -match 'agent\.run\.trunk') -and ($trunkText -match '"finalize_run"')
    Write-Host ("  CS15 run 主干在册（正对照：主干配方文件={0} / OrderedRecipe<={1} / 主干步骤声明={2}）：须均为 True" -f $trunkExists, $cs15Recipe, $cs15Trunk) -ForegroundColor Gray
    if (-not ($trunkExists -and $cs15Recipe -and $cs15Trunk))
    {
        $violations.Add('[CS15] run 主干未在运行时配方域在册（AgentRunTrunkCircuit.cs 缺失或未声明 agent.run.trunk 顺序链）')
    }

    # CS16 负对照：AgentRunner 主干禁命令式内联（原 using var ports = BuildRunPorts( 序列已收敛为配方驱动）。
    $cs16Inline = (Test-Path $agentRunnerFile) -and
        ((Get-Content -LiteralPath $agentRunnerFile -Raw -Encoding UTF8) -match 'using var ports = BuildRunPorts\(')
    Write-Host ("  CS16 禁命令式主干（负对照：AgentRunner 内 using var ports = BuildRunPorts( 内联）：{0} 处（须为 0）" -f ([int]$cs16Inline)) -ForegroundColor Gray
    if ($cs16Inline)
    {
        $violations.Add('[CS16] run 主干出现命令式内联序列（应经 AgentRunTrunkCircuit 配方驱动）')
    }

    # ---------- CS17 受控第二点（负对照：兼容入口消费方封顶） ----------
    # 已知面 = 定义处（RoundLoopDriver）+ 既有两消费方（RuleTaskExecutor / StatelessAnswerLoop）；
    # 其余引用 = 平行消费方爬升（受控第二点决议：新增消费方须先过 P4 迁移，非本门禁放行）。
    $singleRootKnown = @(
        (Join-Path $agentLoopsDir 'RoundLoopDriver.cs'),
        (Join-Path $agentLoopsDir 'TaskPool\RuleTaskExecutor.cs'),
        (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Answer\StatelessAnswerLoop.cs'))
    $singleRootCallers = @()
    foreach ($scanDir in @($agentLoopsDir, (Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules')))
    {
        if (-not (Test-Path $scanDir)) { continue }
        $singleRootCallers += @(Get-ChildItem -Path $scanDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
            Select-String -Pattern 'RunSingleRootAsync\s*\(' -ErrorAction SilentlyContinue |
            ForEach-Object { $_.Path })
    }
    $singleRootCallers = @($singleRootCallers | Sort-Object -Unique)
    $cs17Stray = @($singleRootCallers | Where-Object { $singleRootKnown -notcontains $_ })
    Write-Host ("  CS17 受控第二点（负对照：RunSingleRootAsync 已知消费方之外的引用文件数）：{0} 处（须为 0）" -f $cs17Stray.Count) -ForegroundColor Gray
    if ($cs17Stray.Count -gt 0)
    {
        $violations.Add("[CS17] RunSingleRootAsync 出现已知消费方之外的引用 $($cs17Stray.Count) 处（受控第二点：新增消费方须先过 P4 迁移决议）")
    }
    if ($ShowDetail) { foreach ($h in $cs17Stray) { Write-Host ("    {0}" -f ($h -replace [regex]::Escape($Root + '\'), '')) -ForegroundColor DarkGray } }

    # ---------- CS18 轮内链声明面在册（正对照：R1/R3 不回退） ----------
    $flowMapperFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\Circuit\RoundFlowSpecMapper.cs'
    $specLoaderFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Core\Nodes\Loading\RoundSpecLoader.cs'
    $cs18Enforce = (Test-Path $flowMapperFile) -and
        ((Get-Content -LiteralPath $flowMapperFile -Raw -Encoding UTF8) -match 'EnforceDeclaredEdgeCoverage')
    $cs18Apply = (Test-Path $specLoaderFile) -and
        ((Get-Content -LiteralPath $specLoaderFile -Raw -Encoding UTF8) -match 'ApplyDeclaredDependencies')
    Write-Host ("  CS18 轮内链声明面在册（正对照：声明覆盖判据={0} / 声明反解={1}）：须均为 True" -f $cs18Enforce, $cs18Apply) -ForegroundColor Gray
    if (-not ($cs18Enforce -and $cs18Apply))
    {
        $violations.Add('[CS18] 轮内链声明面缺失（未声明真依赖判红 / [Matrix] 反解未在册）；R1/R3 回退')
    }
}
else
{
    Write-Host "  CS12/CS13 路径缺失（Agent.Core\Loops 未找到）：跳过" -ForegroundColor DarkGray
}

# ---------- CS14 会话链单驱动（负对照：SessionWorkflow 禁第二套管线引擎 / 正对照：区域电路在册） ----------
$convDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Orchestration\SessionWorkflow'
if (Test-Path $convDir)
{
    $convCircuitDir = Join-Path $convDir 'Circuit'
    $convCs = @(Get-ChildItem -Path $convDir -Filter *.cs -File -ErrorAction SilentlyContinue)
    $convCircuitCs = @(Get-ChildItem -Path $convCircuitDir -Filter *.cs -File -ErrorAction SilentlyContinue)
    $convAll = @($convCs + $convCircuitCs)

    # 会话**发送链**（逐消息生命周期）文件集：不含 FoldStepPipeline（折叠步进容器，另一关注点，合法消费 Modules.Pipeline 组合子）。
    $convChainNames = @('ConversationCentralPipeline.cs', 'ConversationPipeline.cs', 'ConversationSessionService.cs')
    $convChainCs = @($convAll | Where-Object { $convChainNames -contains $_.Name })

    # 负对照：会话链禁引用第二套管线引擎（Modules.CentralPipeline / Modules.Pipeline 槽位）
    $cs14 = @($convChainCs | Select-String -Pattern 'Modules\.CentralPipeline|Modules\.Pipeline|new PipelineBuilder<' -ErrorAction SilentlyContinue)
    Write-Host ("  CS14 会话链单驱动（负对照：SessionWorkflow 内 Modules.CentralPipeline/Pipeline 引用）：{0} 处（须为 0）" -f $cs14.Count) -ForegroundColor Gray
    if ($cs14.Count -gt 0)
    {
        $violations.Add("[CS14] 会话链出现第二套管线引擎 $($cs14.Count) 处（逐消息链须由 CircuitEngine 单驱动）")
    }

    # 正对照：区域电路在册（区域节点 + Once 计划 + 会话链入口）
    $convRegionFile = Join-Path $convCircuitDir 'ConversationSendRegionCircuit.cs'
    $cs14Region = (Test-Path $convRegionFile) -and
        ((Get-Content -LiteralPath $convRegionFile -Raw -Encoding UTF8) -match 'RegionNodeBase\.For')
    $cs14Once = (Test-Path $convRegionFile) -and
        ((Get-Content -LiteralPath $convRegionFile -Raw -Encoding UTF8) -match 'RegionIterationPlan\.Once')
    $cs14Entry = @($convChainCs | Select-String -Pattern 'ExecuteSendRegionAsync' -ErrorAction SilentlyContinue).Count -ge 1
    Write-Host ("  CS14-d 会话链区域电路在册（正对照：区域节点={0} / Once 计划={1} / send 入口={2}）：须均为 True" -f $cs14Region, $cs14Once, $cs14Entry) -ForegroundColor Gray
    if (-not ($cs14Region -and $cs14Once -and $cs14Entry))
    {
        $violations.Add('[CS14] 会话链区域电路执行面缺失（区域节点/Once 计划/send 入口未在册）')
    }
}
else
{
    Write-Host "  CS14 路径缺失（Agent.Orchestration\SessionWorkflow 未找到）：跳过" -ForegroundColor DarkGray
}

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 电路语义守卫：0 违归（唯一宿主已接线 / 无平行引用抽象 / 引用符号齐备 / 轮骨架唯一接线 / 装配域唯一接线 / 接入面编译入口唯一 / 编排动作分派配方化 / 区域子计划编译入口唯一 / 区域镜像单一来源 / 递归执行化在册 / 分支分派执行化在册 / 调用执行化在册 / 全流程图渲染在册 / 轮间链单驱动 / 区域化轮循环在册 / 会话链单驱动 / run 主干配方化在册 / 受控第二点封顶 / 轮内链声明面在册）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 电路语义守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0