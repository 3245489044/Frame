#requires -Version 5.1
<#
.SYNOPSIS
    运行收敛守卫（RunConvergence）—— 把「目标收敛到工程」由运行器内部实现变为机器判据（过程层缺陷 P6）。

.DESCRIPTION
    根因（框架级缺陷 · 收敛通道缺失）：`-Class X` 在未给 -Project 时无收敛通道 ⇒ 候选集取全部测试工程，
    把 1 个运行单元放大到 ~N（全部工程），其中绝大多数 [EMPTY] 零匹配白跑
    （对齐 .clinerules\axioms.md % ZERO_YIELD_LIMIT「单轮零产出调用上限」）。

    判据（仅以运行器的**可观测输出**为准，不读私有实现）：
      C0 正对照：判据载体（输出解析器）对合成样本行为正确（防判据恒真/恒假）。
      C1 精确类名收敛：取清单内「恰属 1 个工程的类」，`-Class <该类> -DryRun` 须解析出**恰 1** 个运行单元。
      C2 未命中 fail-fast：`-Class <不存在的类> -DryRun` 须非零退出，且**不得**产出运行计划
         （禁静默回落全工程扫描）。
      C3 归并（单元粒度 = 工程）：同工程的两个类 `-Class A,B` 须解析出**恰 1** 个单元（filter 为 OR）。

    模式：默认报告模式（列违规，不设退出码）；-Check 校验模式（违规 → 退出码 1）。只读仓库（零侧写）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐判据实测明细。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 仓库超根（含 Frame\ 与 Projects\）
$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

$centralTest = Join-Path $repoRoot 'Environment\CentralTest'
$runner = Join-Path $centralTest 'Internal\Run-CSharpTests.ps1'
$modulePath = Join-Path $centralTest 'TestRunner\TestRunner.psm1'

$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $runner)) { Write-Error "运行器不存在：$runner"; exit 2 }
if (-not (Test-Path -LiteralPath $modulePath)) { Write-Error "TestRunner 模块不存在：$modulePath"; exit 2 }

Import-Module $modulePath -Force -DisableNameChecking

# ── 判据载体：运行器 DryRun 输出 → 运行单元数（唯一定义点；C0 正/负对照共用同一解析器）──
function Get-PlannedUnitCount([string]$Output) {
    $m = [regex]::Match([string]$Output, '计划运行单元:\s*(?<n>\d+)\s*个')
    if (-not $m.Success) { return -1 }
    return [int]$m.Groups['n'].Value
}

function Invoke-RunnerDryRun([string[]]$RunnerArgs) {
    $out = & pwsh -NoProfile -File $runner @RunnerArgs 2>&1 | Out-String
    return @{ Output = $out; ExitCode = $LASTEXITCODE }
}

# ── C0 正对照：解析器对合成样本 │ 防判据空转 ──
$probeLine = '  计划运行单元: 7 个'
if ((Get-PlannedUnitCount -Output $probeLine) -ne 7) {
    $violations.Add('[C0] 正对照失效：输出解析器未把合成样本判为 7 个单元（判据载体被改坏）')
}
if ((Get-PlannedUnitCount -Output '（无计划行）') -ne -1) {
    $violations.Add('[C0] 负对照失效：缺计划行时未返回 -1（判据载体恒真）')
}

# ── 样本：从清单取「恰属 1 个工程的类」与「同工程第二个类」 ──
$inv = Get-TestInventory -Root $centralTest
$singles = @()
foreach ($k in $inv.Classes.get_Keys()) {
    $projs = @($inv.Classes[$k])
    if ($projs.Count -eq 1) { $singles += [pscustomobject]@{ Class = $k; Project = [string]$projs[0] } }
}
$sampleA = $singles | Where-Object { $_.Class -match 'Tests$' } | Select-Object -First 1
$sampleB = $null
if ($sampleA) {
    $twinCandidates = @($singles | Where-Object { $_.Project -eq $sampleA.Project -and $_.Class -ne $sampleA.Class })
    if ($twinCandidates.Count -gt 0) { $sampleB = $twinCandidates[0] }
}
if (-not $sampleA) {
    $violations.Add('[C0] 正对照失效：清单内无「恰属 1 个工程」的测试类（判据无标的）')
}

# ── C1 精确类名收敛 → 恰 1 单元 ──
if ($sampleA) {
    $r1 = Invoke-RunnerDryRun @('-Class', $sampleA.Class, '-DryRun')
    $n1 = Get-PlannedUnitCount -Output $r1.Output
    if ($ShowDetail) { Write-Host ("  C1 类={0} → 单元={1}（exit={2}）" -f $sampleA.Class, $n1, $r1.ExitCode) -ForegroundColor DarkGray }
    if ($r1.ExitCode -ne 0) {
        $violations.Add("[C1] 精确类名 '$($sampleA.Class)' DryRun 非零退出（$($r1.ExitCode)）——收敛路径被破坏")
    } elseif ($n1 -ne 1) {
        $violations.Add("[C1] 精确类名 '$($sampleA.Class)' 解析出 $n1 个运行单元（应恰 1）——收敛失效（放大白跑）")
    }
}

# ── C2 未命中 → fail-fast（非零退出 ∧ 无计划） ──
$probeMissing = '__NoSuchTestClass_RunConvergenceProbe__'
$r2 = Invoke-RunnerDryRun @('-Class', $probeMissing, '-DryRun')
$n2 = Get-PlannedUnitCount -Output $r2.Output
if ($ShowDetail) { Write-Host ("  C2 不存在的类 → exit={0} 单元={1}" -f $r2.ExitCode, $n2) -ForegroundColor DarkGray }
if ($r2.ExitCode -eq 0) {
    $violations.Add('[C2] 未命中的 -Class 未 fail-fast（退出码 0）——静默回落全工程扫描')
}
if ($n2 -ge 0) {
    $violations.Add("[C2] 未命中的 -Class 仍产出运行计划（$n2 个单元）——禁静默放大")
}

# ── C3 归并：同工程两类 → 恰 1 单元 ──
if ($sampleA -and $sampleB) {
    $r3 = Invoke-RunnerDryRun @('-Class', ("{0},{1}" -f $sampleA.Class, $sampleB.Class), '-DryRun')
    $n3 = Get-PlannedUnitCount -Output $r3.Output
    if ($ShowDetail) { Write-Host ("  C3 {0},{1} → 单元={2}" -f $sampleA.Class, $sampleB.Class, $n3) -ForegroundColor DarkGray }
    if ($r3.ExitCode -ne 0) {
        $violations.Add("[C3] 同工程两类 DryRun 非零退出（$($r3.ExitCode)）")
    } elseif ($n3 -ne 1) {
        $violations.Add("[C3] 同工程两类未归并（解析出 $n3 个单元，应恰 1）——单元粒度非工程")
    }
}

Write-Host ("  清单：工程 {0} / 类 {1}｜样本 A={2} B={3}" -f $inv.ProjectCount, $inv.Classes.Count,`
        $(if ($sampleA) { $sampleA.Class } else { '(无)' }), $(if ($sampleB) { $sampleB.Class } else { '(无)' })) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] RunConvergence：0 违规（精确类名收敛恰 1 单元 ∧ 未命中 fail-fast ∧ 同工程归并）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] RunConvergence 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：-Class 一律先收敛到「声明该类的工程」（TestRunner: Get-TestClassProjectIndex/Get-TestInventory），未命中 fail-fast；运行单元粒度 = 工程。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0