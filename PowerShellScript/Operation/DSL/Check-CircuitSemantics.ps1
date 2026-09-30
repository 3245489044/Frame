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
Write-Host "  电路语义守卫（唯一宿主接线 / 禁平行引用抽象 / 符号在册 / 轮骨架唯一接线 / 装配域唯一接线）" -ForegroundColor Cyan
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

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 电路语义守卫：0 违归（唯一宿主已接线 / 无平行引用抽象 / 引用符号齐备 / 轮骨架唯一接线 / 装配域唯一接线）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 电路语义守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0