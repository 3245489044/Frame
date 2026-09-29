#requires -Version 5.1
<#
.SYNOPSIS
    引用框架禁引守卫（ReferenceExclusion）—— 「不引 UR 写边语义 / 第二执行器 / 产物存储面」的机器判据（B5）。

.DESCRIPTION
    沿《引用关系模块化 × 工作流 / UniversalReference 引入 × 组装归流收尾 —— 综合方案》§3.1 / §3.3-B5：
    UniversalReference 只以「词表 DSL + 图算法只读服务 + 资源模型」三态引入；
    其**写边语义**（封闭 6 值枚举会塌缩自由字符串词表，且形成第二写边路径）、**第二套执行器**、**产物/存储面**与
    **工程面分支**一律禁引。

    判据（C1 硬判据；C2 正对照；C3 负对照）：
      C1 禁引清单在两个扫描面内 **0 命中**：
           · Projects\Plug\Agent\src                      ← 全部 5 个模式
           · Frame\CsharpPrj\Operation\DataStoreDriven    ← 除 IUniversalReferenceManager 外的 4 个模式
             （IUniversalReferenceManager 在该层有**合法**消费面：适配器与 ProviderFactory 装配；
               禁的只是「Agent 侧把它当写边通道」⇒ Agent 面 0 命中即满足）
      C2 正对照（防假绿）：适配器面**必须**命中 IUniversalReferenceManager（扫描面有效）
      C3 负对照（防假绿）：合成含 `new WorkflowExecutor(` 的临时 .cs 必须被**同一判据函数**命中 C1

    判据载体 = 源码结构事实（符号出现位置），不引入执行者叙述（SEP.JudgmentCarrierStructural）。
    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数。

.EXAMPLE
    .\Check-ReferenceExclusion.ps1 -Check
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

# ---- 禁引清单（5 模式；Agent 面全禁）----
$ForbiddenPatterns = @(
    'WorkflowExecutor',                                     # 第二套执行器（与 [Flow]→RoundSpec→FoldEngine 冲突）
    'IWorkflowExecutor',
    'IUniversalReferenceManager',                           # 作写边实现（封闭枚举塌缩词汇 + 第二写边路径）
    'UniversalWorkflowFramework.Reference.Relationship',    # 复合 RELID 产物/存储面
    'BranchManagement'                                      # 工程面分支（语言层分支 = [Matrix] 行）
)

$AgentScope = 'Projects\Plug\Agent\src'
$StoreScope = 'Frame\CsharpPrj\Operation\DataStoreDriven'
$StoreAllowedPatterns = @('IUniversalReferenceManager')

# ---- C2 正对照：适配器面必须命中 IUniversalReferenceManager ----
$PositiveControlFile = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Core\UniversalReferenceResourceReferenceStore.cs'

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  引用框架禁引守卫（B5：不引 UR 写边 / 第二执行器 / 产物存储面 / 工程分支）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---------------- 判据函数（C1/C3 共用同一实现） ----------------
function Get-ForbiddenHits {
    param([string]$Dir, [string[]]$Patterns)

    $hits = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Dir)) { return $hits }
    foreach ($f in (Get-ChildItem -LiteralPath $Dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' })) {
        $index = 0
        foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
            $index++
            $t = $line.Trim()
            if ($t.StartsWith('//') -or $t.StartsWith('/*') -or $t.StartsWith('*')) { continue }
            foreach ($p in $Patterns) {
                if ($line.Contains($p)) { $hits.Add([pscustomobject]@{ Path = $f.FullName; Line = $index; Pattern = $p }) }
            }
        }
    }
    return $hits
}

# ---------------- C1 禁引判定 ----------------
$agentHits = Get-ForbiddenHits -Dir (Join-Path $Root $AgentScope) -Patterns $ForbiddenPatterns
$storePatterns = $ForbiddenPatterns | Where-Object { $StoreAllowedPatterns -notcontains $_ }
$storeHits = Get-ForbiddenHits -Dir (Join-Path $Root $StoreScope) -Patterns $storePatterns

foreach ($h in $agentHits) {
    $rel = $h.Path.Substring($Root.Length).TrimStart('\')
    $violations.Add("[C1] $rel`:$($h.Line) 命中禁引模式 ""$($h.Pattern)"" —— UR 只引词表/只读算法/资源模型，不引写边与执行器")
}
foreach ($h in $storeHits) {
    $rel = $h.Path.Substring($Root.Length).TrimStart('\')
    $violations.Add("[C1] $rel`:$($h.Line) 命中禁引模式 ""$($h.Pattern)""（原语层）")
}

# ---------------- C2 正对照 ----------------
$positiveHit = (Test-Path -LiteralPath $PositiveControlFile) -and
    (@(Get-ForbiddenHits -Dir (Split-Path $PositiveControlFile -Parent) -Patterns @('IUniversalReferenceManager')) |
        Where-Object { $_.Path -eq $PositiveControlFile }).Count -gt 0
if (-not $positiveHit) {
    $violations.Add('[C2] 正对照失效：适配器面未命中 IUniversalReferenceManager（扫描面或文件缺失？判据恒真风险）')
}

# ---------------- C3 负对照 ----------------
$probeDir = Join-Path ([System.IO.Path]::GetTempPath()) ('refexcl-probe-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
$probePath = Join-Path $probeDir 'Probe.cs'
@'
namespace Probe;
public static class Probe
{
    public static object Build() => new WorkflowExecutor();
}
'@ | Set-Content -LiteralPath $probePath -Encoding utf8
try {
    $probeHits = Get-ForbiddenHits -Dir $probeDir -Patterns $ForbiddenPatterns
    if (@($probeHits).Count -eq 0) {
        $violations.Add('[C3] 负对照失效：合成禁引未命中（判据恒真/恒假？）')
    }
}
finally {
    if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Force -Recurse }
}

# ---------------- 汇总 ----------------
Write-Host ''
Write-Host ("  实测：Agent 面命中 {0} · 原语层面命中 {1} · 禁引模式 {2} 个" -f `
    $agentHits.Count, $storeHits.Count, $ForbiddenPatterns.Count) -ForegroundColor Gray
if ($ShowDetail) {
    Write-Host ("  扫描面 : {0} / {1}" -f $AgentScope, $StoreScope) -ForegroundColor Gray
    Write-Host ("  正对照 : {0}（须命中 IUniversalReferenceManager）" -f (Split-Path $PositiveControlFile -Leaf)) -ForegroundColor Gray
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 引用框架禁引守卫：0 违规（C1 禁引清单两扫描面零命中；C2/C3 正负对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 引用框架禁引守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host '  修复口径：UR 只引「词表 DSL + 图算法只读服务 + 资源模型」；写边恒走 ReferenceGraphCoordinator。' -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
