#requires -Version 5.1
<#
.SYNOPSIS
    节点 loc 单源一致性守卫（NodeLocConsistency）—— 「loc 值单源」+「读侧位置来源 = loc 反解」的机器判据。

.DESCRIPTION
    扫描面：Frame\CsharpPrj、Projects 下的 *.cs（排除 obj / bin / BuildOutput / _deprecated）。

    判据：
      C1 loc 字面量定义面：`"agent-round"` 只允许出现在**定义清单**（单源常量 / 镜像常量 / 契约默认值 / 注释说明）；
                          其它文件出现即违规（治「各调用点各自持有 loc 字面量」—— HV-1 / U12 L-7）。
      C2 回退基线（只减不增）：inline 回退（如 `?? "agent-round"`）仅允许出现在已登记的**债务清单**文件；
                          新增即违规（清单只减不增 ⇒ 逐处收敛为单源）。
      C3 读侧位置来源：`new ReadScope(` 构造点须带 `Loc:`（L-6：读侧位置集合 = loc 反解产物，与写侧同一
                          `IPlacementResolver`；禁新增绕过 loc 的读侧）。
      C4 正对照（防假绿）：① 单源文件确实含 `DefaultWorkflowId` 字面量定义；② 位置反解声明面确实含 `[Space] Loc`
                          坐标；③ `ReadScope` 构造点数 ≥ 基线（读侧被删而门禁恒绿 = 失效）。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（C1–C3）或正对照失效（C4）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-NodeLocConsistency.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodeLocConsistency.ps1 -Check
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

# ---------------- 基线（只减不增；变更须给出理由并同步本表） ----------------
$LocLiteral = 'agent-round'

# 定义面：单源常量 / 镜像常量 / 契约默认值 / 注释说明（允许出现字面量）
$DefinitionFiles = @(
    'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\NodeKeys.cs',                       # loc 单源（唯一权威）
    'Frame\CsharpPrj\Operation\DataStoreDriven\Core\WorkflowIds.cs',                      # 镜像（Agent-无关）
    'Frame\CsharpPrj\Operation\HierarchyMethod\Reference\EntityRef.cs',                   # 引用域默认值
    'Frame\CsharpPrj\Share\Foundations\ScopeFramework\Naming\L0ScopeNamingRule.cs',       # L0 兼容默认
    'Frame\CsharpPrj\Operation\WorkFlowMethod\Nodes\Contracts\NodeOutput.cs',             # 契约默认值
    'Frame\CsharpPrj\Operation\WorkFlowMethod\Nodes\Contracts\NodeSignal.cs',             # 契约默认值
    'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Workflows\WorkflowSpecLoader.cs',    # 工作流缺省
    'Projects\Plug\Agent\src\Agent.Core\Nodes\RoundStepInvoker.cs'                        # 注释（单一权威说明）
)

# 债务基线（inline 回退；只减不增 —— 收敛后须从本表删除）
$FallbackBaselineFiles = @(
    'Projects\Plug\Agent\src\Agent.Core\Loops\TaskOrchestrateFunction.cs',
    'Projects\Plug\Agent\src\Agent.Launcher\Api\Plan\WritePlanner.cs',
    'Projects\Plug\Agent\src\Agent.Launcher\Modules\Libraries\AgentUiLibraryTaskPool.cs',
    'Projects\Plug\Agent\src\Agent.Launcher\Modules\Routing\AgentInvocationRouter.cs'
)

$ReadScopeBaseline = 4
$singleSourceFile  = 'Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\NodeKeys.cs'
$declarationFile   = 'Projects\Plug\Agent\Config\nodes\placement.ushar'

$excludePattern = '\\(obj|bin|BuildOutput|_deprecated)\\'
$scanRoots = @('Frame\CsharpPrj', 'Projects')

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  节点 loc 单源一致性守卫（loc 单源 / 回退只减不增 / 读侧 L-6）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# ---------------- 扫描面 ----------------
$files = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $scanRoots) {
    $absolute = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $absolute)) { continue }
    Get-ChildItem -LiteralPath $absolute -Recurse -Force -Filter *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch $excludePattern } |
        ForEach-Object { $files.Add($_.FullName) }
}

if ($files.Count -eq 0) {
    Write-Host "[FAIL] 扫描面为空（$($scanRoots -join ' / ')）⇒ 判据不可判，禁静默通过" -ForegroundColor Yellow
    exit 1
}

function Get-Relative([string]$FullName) { return $FullName.Substring($Root.Length).TrimStart('\') }

# ---------------- C1 / C2 loc 字面量 ----------------
$definitionHits = 0
$fallbackHits = [System.Collections.Generic.Dictionary[string, int]]::new([System.StringComparer]::OrdinalIgnoreCase)
$strayHits = [System.Collections.Generic.List[string]]::new()
$literalPattern = [regex]::Escape('"' + $LocLiteral + '"')

foreach ($file in $files) {
    $relative = Get-Relative $file
    # 强制数组：单行文件 Get-Content 返回 [string]，索引会取到「首字符」而非整行（判据会假绿）
    $lines = @(Get-Content -LiteralPath $file -ErrorAction SilentlyContinue)
    if ($lines.Count -eq 0) { continue }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ([string]::IsNullOrEmpty($lines[$i])) { continue }
        if ($lines[$i] -notmatch $literalPattern) { continue }

        if ($DefinitionFiles -contains $relative) { $definitionHits++; continue }
        if ($FallbackBaselineFiles -contains $relative) {
            if (-not $fallbackHits.ContainsKey($relative)) { $fallbackHits[$relative] = 0 }
            $fallbackHits[$relative]++
            continue
        }

        $strayHits.Add("$relative`:$($i + 1)")
    }
}

foreach ($hit in $strayHits) {
    $violations.Add("[C1] loc 字面量 '$LocLiteral' 出现在未登记文件：$hit（loc 单源 = NodeKeys.DefaultWorkflowId）")
}

# ---------------- C3 读侧位置来源 ----------------
$readScopeSites = 0
$readScopeWithLoc = 0
$readScopePattern = 'new\s+(global::)?[A-Za-z0-9_.]*ReadScope\s*\('

foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrEmpty($text)) { continue }

    foreach ($match in [regex]::Matches($text, $readScopePattern)) {
        $readScopeSites++
        $tail = $text.Substring($match.Index)
        $end = $tail.IndexOf(');', [System.StringComparison]::Ordinal)
        $block = if ($end -ge 0) { $tail.Substring(0, $end + 2) } else { $tail }

        if ($block -match '(^|[\s(,])Loc\s*:') {
            $readScopeWithLoc++
        }
        else {
            $line = ($text.Substring(0, $match.Index) -split "`n").Count
            $violations.Add("[C3] ReadScope 构造点缺 loc：$(Get-Relative $file):$line" +
                            "（L-6：读侧位置集合须由 IPlacementResolver 反解得出，禁绕过）")
        }
    }
}

# ---------------- C4 正对照（防假绿） ----------------
$singleSourcePath = Join-Path $Root $singleSourceFile
$singleSourceOk = (Test-Path -LiteralPath $singleSourcePath) -and
    ((Get-Content -LiteralPath $singleSourcePath -Raw) -match ('DefaultWorkflowId\s*=\s*"' + [regex]::Escape($LocLiteral) + '"'))
if (-not $singleSourceOk) {
    $violations.Add("[C4] 正对照失效：单源文件未含 DefaultWorkflowId 定义（$singleSourceFile）")
}

$declarationPath = Join-Path $Root $declarationFile
$declarationOk = (Test-Path -LiteralPath $declarationPath) -and
    ((Get-Content -LiteralPath $declarationPath -Raw) -match '\[Space\]\s+Loc\b')
if (-not $declarationOk) {
    $violations.Add("[C4] 正对照失效：位置反解声明面未含 [Space] Loc（$declarationFile）")
}

if ($readScopeSites -lt $ReadScopeBaseline) {
    $violations.Add("[C4] 正对照失效：ReadScope 构造点 $readScopeSites < 基线 $ReadScopeBaseline（读侧被删 ⇒ 判据恒绿）")
}

# ---------------- 输出 ----------------
Write-Host ""
Write-Host "  实测：C1 定义面命中 $definitionHits 处 / C2 回退命中 $($fallbackHits.Count) 文件" -ForegroundColor Gray
Write-Host "        C3 ReadScope 构造点 $readScopeWithLoc/$readScopeSites 带 Loc" -ForegroundColor Gray

if ($ShowDetail) {
    Write-Host "  [C1] 定义面清单 $($DefinitionFiles.Count) 件；实测命中 $definitionHits 处" -ForegroundColor Gray
    Write-Host "  [C2] 回退债务清单 $($FallbackBaselineFiles.Count) 件；实测命中 $($fallbackHits.Count) 件" -ForegroundColor Gray
}

foreach ($entry in $fallbackHits.GetEnumerator()) {
    Write-Host "  [C2] 回退（债务基线，只减不增）：$($entry.Key)" -ForegroundColor DarkYellow
}

if ($violations.Count -eq 0) {
    Write-Host ""
    Write-Host "[OK] 节点 loc 单源一致性守卫：0 违规（loc 单源且读侧位置来源 = 反解）" -ForegroundColor Green
    exit 0
}

Write-Host ""
foreach ($violation in $violations) {
    Write-Host "  $violation" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "[FAIL] 节点 loc 单源一致性守卫：$($violations.Count) 处违规" -ForegroundColor Red
if ($Check) { exit 1 }
exit 0

