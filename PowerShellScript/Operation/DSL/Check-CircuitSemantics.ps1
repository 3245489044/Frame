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

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 电路语义守卫：0 违归（唯一宿主已接线 / 无平行引用抽象 / 引用符号齐备 / 轮骨架唯一接线 / 装配域唯一接线 / 接入面编译入口唯一 / 编排动作分派配方化）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 电路语义守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0