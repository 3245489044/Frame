#requires -Version 5.1
<#
.SYNOPSIS
    引用关系词汇守卫（ReferenceVocabulary）—— 「边类型 / 端点类型字面量只出现一处」的机器判据（A 线 A1）。

.DESCRIPTION
    沿《引用关系模块化 × 工作流 / UniversalReference 引入 × 组装归流收尾 —— 综合方案》§2.3-A1：
    引用关系词汇（边类型 + 端点类型）的**唯一字面量源** =
    `Frame\CsharpPrj\Operation\DataStoreDriven\Abstractions\ReferenceTypes.cs`；
    其余常量类（`CrossDbReferenceTypes` / `WorkflowReferenceTypes` / `NodeResourceTypes` /
    `LoopFoldMethod.Reference.Model.ReferenceTypes`）一律**别名转发**，不得再出现字面量。

    扫描面（仅 `*.cs`；不含 bin/obj；不含纯注释行）：
      Frame\CsharpPrj · Projects\Plug\Agent\src · Environment\CentralTest\AgentTest

    判据（C1 硬判据；C2 零依赖层豁免；C3 正对照；C4 负对照）：
      C1 唯一字面量源：词汇字面量只允许出现在 `ReferenceTypes.cs`（单源）与 C2 豁免面
                         —— 其余任何位置命中 ⇒ 违规（第二词表 / 常量未收敛）
      C2 零依赖层豁免（**只减不增**，白名单登记）：
                         `Share\Foundations\ScopeFramework`（Layer 3，零项目引用）与
                         `Modules\DeclarativeDataflow`（Layer 2，零项目引用）物理上无法引用单源
                         ⇒ 其本地字面量按「层内自治常量」保留，计数 ≤ 基线
      C3 正对照（防假绿）：单源文件命中数 ≥ 基线（单源被掏空 ⇒ 判据恒真）
      C4 负对照（防假绿）：合成含 `"cross_db_link"` 的临时 .cs 必须被**同一判据函数**命中 C1

    判据载体 = 源码结构事实（文件位置 + 字面量计数），不引入执行者叙述（SEP.JudgmentCarrierStructural）。
    运行时 counterpart = `Check-NodeHierarchyRefs` E2（声明 `edge=` 集 ⇄ 写边实测集双向对账）。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（C1 / C3）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-ReferenceVocabulary.ps1 -ShowDetail

.EXAMPLE
    .\Check-ReferenceVocabulary.ps1 -Check
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

# ---- 词汇（字面量判据对象；与单源 ReferenceTypes.cs 的常量值逐字一致）----
$Vocabulary = @(
    'cross_db_link', 'session_owns', 'task_owns_task', 'history_owns_task', 'history_owns_node',
    'node_to_node', 'keymapping_to_node',
    'Calls', 'Owns', 'Exports', 'Inherits', 'Implements', 'DependsOn'
)

# ---- 唯一字面量源 ----
$singleSource = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Abstractions\ReferenceTypes.cs'

# ---- 扫描面 ----
$scanRoots = @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src'),
    (Join-Path $Root 'Environment\CentralTest\AgentTest')
)

# ---- C2 零依赖层豁免（只减不增）----
# key = 相对仓库根的路径片段；value = 允许的字面量命中数上限
$ExemptBaselines = [ordered]@{
    'Frame\CsharpPrj\Share\Foundations\ScopeFramework' = 2   # EnsureTaskNodeEdgeStep：零项目引用，无法引用单源
    'Frame\CsharpPrj\Modules\DeclarativeDataflow'      = 3   # BuiltinHierarchy：零项目引用，无法引用单源
}
# ---- C3 正对照基线：单源文件**边类型**字面量数下限 ----
# 13 = 归属边 5（cross_db_link/session_owns/task_owns_task/history_owns_task/history_owns_node）
#    + 工作流边 6（Calls/Owns/Exports/Inherits/Implements/DependsOn）+ 节点关系边 2（node_to_node/keymapping_to_node）。
# 端点类型（node/keymapping 经 NodeTypes 单源；task/session/history/workflow）同为单源定义，但因其名过短易误判，
# 不入本判据的词表（其唯一性由单源文件本身 + 消费面均为常量引用保证）。
$SingleSourceLiteralBaseline = 13

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  引用关系词汇守卫（A1：边类型/端点类型字面量只住一处）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $singleSource)) {
    Write-Host "[FAIL] 未找到唯一字面量源：$singleSource" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------------- 判据函数（C1/C4 共用同一实现） ----------------
function Get-VocabularyHits {
    param([string[]]$Paths)

    $hits = [System.Collections.Generic.List[object]]::new()
    foreach ($f in $Paths) {
        if ($f -match '\\obj\\' -or $f -match '\\bin\\') { continue }
        $index = 0
        foreach ($line in [System.IO.File]::ReadAllLines($f)) {
            $index++
            $t = $line.Trim()
            # 纯注释行不构成字面量来源（判据载体 = 代码字面量）
            if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
            foreach ($name in $Vocabulary) {
                if ($line.Contains('"' + $name + '"')) {
                    $hits.Add([pscustomobject]@{ Path = $f; Line = $index; Name = $name })
                }
            }
        }
    }
    return $hits
}

# ---------------- 扫描面文件枚举 ----------------
$files = [System.Collections.Generic.List[string]]::new()
foreach ($r in $scanRoots) {
    if (-not (Test-Path -LiteralPath $r)) { continue }
    foreach ($f in (Get-ChildItem -LiteralPath $r -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue)) {
        if ($f.FullName -match '\\obj\\' -or $f.FullName -match '\\bin\\') { continue }
        $files.Add($f.FullName)
    }
}

$allHits = Get-VocabularyHits -Paths $files.ToArray()
$singleSourceHits = @($allHits | Where-Object { $_.Path -eq $singleSource })

# ---------------- C1 / C2 ----------------
$exemptCounts = @{}
foreach ($key in $ExemptBaselines.Keys) { $exemptCounts[$key] = 0 }

foreach ($h in $allHits) {
    if ($h.Path -eq $singleSource) { continue }
    $rel = $h.Path.Substring($Root.Length).TrimStart('\')
    $matched = $false
    foreach ($key in $ExemptBaselines.Keys) {
        if ($rel.StartsWith($key, [System.StringComparison]::OrdinalIgnoreCase)) {
            $exemptCounts[$key]++
            $matched = $true
            break
        }
    }
    if (-not $matched) {
        $violations.Add("[C1] $rel`:$($h.Line) 出现词汇字面量 ""$($h.Name)"" —— 唯一字面量源 = ReferenceTypes.cs（其余须别名转发）")
    }
}

foreach ($key in $ExemptBaselines.Keys) {
    if ($exemptCounts[$key] -gt $ExemptBaselines[$key]) {
        $violations.Add("[C2] 零依赖层豁免 $key 字面量 $($exemptCounts[$key]) 处 > 基线 $($ExemptBaselines[$key])（只减不增；新增须先收敛到单源）")
    }
}

# ---------------- C3 正对照 ----------------
if ($singleSourceHits.Count -lt $SingleSourceLiteralBaseline) {
    $violations.Add("[C3] 正对照失效：唯一字面量源字面量 $($singleSourceHits.Count) < 基线 $SingleSourceLiteralBaseline（单源被掏空 ⇒ C1 恒真）")
}

# ---------------- C4 负对照（同一判据函数对合成违规必须命中） ----------------
$probeDir = Join-Path ([System.IO.Path]::GetTempPath()) ('refvocab-probe-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
$probePath = Join-Path $probeDir 'Probe.cs'
@'
namespace Probe;
public static class Probe
{
    public const string X = "cross_db_link";
}
'@ | Set-Content -LiteralPath $probePath -Encoding utf8
try {
    $probeHits = Get-VocabularyHits -Paths @($probePath)
    if (@($probeHits).Count -eq 0) {
        $violations.Add('[C4] 负对照失效：合成字面量未被 C1 判据命中（判据恒真/恒假？）')
    }
}
finally {
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath -Force }
    if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Force -Recurse }
}

# ---------------- 汇总 ----------------
Write-Host ''
Write-Host ("  实测：扫描文件 {0} / 词汇命中 {1} / 单源命中 {2}" -f `
    $files.Count, $allHits.Count, $singleSourceHits.Count) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($key in $ExemptBaselines.Keys) {
        Write-Host ("  零依赖豁免 : {0} = {1} / 基线 {2}" -f $key, $exemptCounts[$key], $ExemptBaselines[$key]) -ForegroundColor Gray
    }
    foreach ($h in $singleSourceHits) {
        Write-Host ("  单源       : {0}:{1} ""{2}""" -f (Split-Path $h.Path -Leaf), $h.Line, $h.Name) -ForegroundColor Gray
    }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 引用关系词汇守卫：0 违规（C1 单源唯一；C2 零依赖层豁免未增长；C3/C4 正负对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 引用关系词汇守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host '  修复口径：字面量唯一源 = Operation.DataStoreDriven.Abstractions.ReferenceTypes；' -ForegroundColor Gray
Write-Host '            其余常量类只做别名转发（const X = ReferenceTypes.X）；' -ForegroundColor Gray
Write-Host '            零依赖层（ScopeFramework / DeclarativeDataflow）保留字面量并登记豁免。' -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
