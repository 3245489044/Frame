#requires -Version 5.1
<#
.SYNOPSIS
    工作流登记守卫（WorkflowRegistration）—— 新增一条声明流时「文件名常量 / 流号映射 / 登记表」三集合须等价。

.DESCRIPTION
    判据（W1–W6）：
      W1 三集合等价：WorkflowSpecFiles 常量值 ≡ DeclaredFlowIds.Map 键 ≡ WorkflowRegistrationCatalog.All 文件名（basename）。
      W2 流号一致：Map[basename] == All.FlowId（禁单侧漂移）。
      W3 声明面存在：每个登记 basename 对应 Config/workflows/<basename>.ushar 存在。
      W4 流号存在：该文件含 `[Flow] <flowId>`（治「声明改了不生效」）。
      W5 未登记声明面：Config/workflows/*.ushar ∖ 三集合，须在基线豁免清单内（显式豁免，禁静默漏登记）。
      W6 正/负对照：登记数 ≥ 基线下限；集合比较函数对合成差异样本必须报出。

    模式：默认报告模式（不设退出码）；-Check 校验模式（违规 → 退出码 1）。
    依赖方向（硬约束）：本件只读仓库；零侧写（不做 B6 派生改写，仅对账）。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking


# 定位仓库根（含 Projects\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name WorkflowRegistration
$minRegistrations = [int]$baseline.MinRegistrations.Value
$unregisteredExempt = @($baseline.UnregisteredExempt.Value)

$violations = [System.Collections.Generic.List[string]]::new()

$specPath = Join-Path $repoRoot 'Projects\Plug\Agent\src\Agent.Core\Configuration\Specs\WorkflowSpecCatalog.cs'
$workflowsDir = Join-Path $repoRoot 'Projects\Plug\Agent\Config\workflows'

function Get-SetDiff {
    param([string[]]$Left, [string[]]$Right)
    return @($Left | Where-Object { $Right -notcontains $_ } | Sort-Object)
}

if (-not (Test-Path -LiteralPath $specPath)) {
    Write-Host "[FAIL] 登记表面缺失：$specPath" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

$text = Get-Content -LiteralPath $specPath -Raw

# ---- 取数：常量（文件名）----
$consts = @{}
foreach ($m in [regex]::Matches($text, 'public const string\s+(?<n>\w+)\s*=\s*"(?<v>[^"]+\.ushar)"')) {
    $consts[$m.Groups['n'].Value] = $m.Groups['v'].Value
}
$constBases = @($consts.Values | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) } | Sort-Object -Unique)

# ---- 取数：Map（basename → flowId）----
$map = @{}
$mapMatch = [regex]::Match($text, 'Dictionary<string,\s*string>\s+Map\s*=\s*new\(StringComparer\.Ordinal\)\s*\{(?<b>[\s\S]*?)\n\s*\};')
if (-not $mapMatch.Success) {
    $violations.Add('W1 取数面失效：DeclaredFlowIds.Map 初始化块未匹配（判据无标的）')
} else {
    foreach ($m in [regex]::Matches($mapMatch.Groups['b'].Value, '\[\s*"(?<k>[^"]+)"\s*\]\s*=\s*"(?<v>[^"]+)"')) {
        $map[$m.Groups['k'].Value] = $m.Groups['v'].Value
    }
}
$mapKeys = @($map.Keys | Sort-Object -Unique)

# ---- 取数：All（常量名 → flowId）----
$all = @{}
foreach ($m in [regex]::Matches($text, 'new Entry\(WorkflowSpecFiles\.(?<c>\w+),\s*"(?<v>[^"]+)"')) {
    $c = $m.Groups['c'].Value
    if ($consts.ContainsKey($c)) { $all[[IO.Path]::GetFileNameWithoutExtension($consts[$c])] = $m.Groups['v'].Value }
    else { $violations.Add("W1 登记引用未定义常量：WorkflowSpecFiles.$c") }
}
$allKeys = @($all.Keys | Sort-Object -Unique)

# ---- W1 三集合等价 ----
foreach ($b in (Get-SetDiff -Left $constBases -Right $mapKeys)) { $violations.Add("W1 常量有、Map 缺：$b") }
foreach ($b in (Get-SetDiff -Left $mapKeys -Right $constBases)) { $violations.Add("W1 Map 有、常量缺：$b") }
foreach ($b in (Get-SetDiff -Left $constBases -Right $allKeys)) { $violations.Add("W1 常量有、登记表缺：$b") }
foreach ($b in (Get-SetDiff -Left $allKeys -Right $constBases)) { $violations.Add("W1 登记表有、常量缺：$b") }

# ---- W2 流号一致 ----
foreach ($b in $mapKeys) {
    if ($all.ContainsKey($b) -and $all[$b] -ne $map[$b]) {
        $violations.Add("W2 流号漂移：$b → Map=$($map[$b]) ≠ All=$($all[$b])")
    }
}

# ---- W3/W4 声明面存在 + 流号在位 ----
foreach ($b in $mapKeys) {
    $file = Join-Path $workflowsDir "$b.ushar"
    if (-not (Test-Path -LiteralPath $file)) {
        $violations.Add("W3 声明文件缺失：Config/workflows/$b.ushar（已登记）")
        continue
    }
    $flowId = $map[$b]
    if ((Get-Content -LiteralPath $file -Raw) -notmatch ('(?m)^\[Flow\]\s+' + [regex]::Escape($flowId) + '\b')) {
        $violations.Add("W4 声明文件未含 [Flow] $flowId：$b.ushar")
    }
}

# ---- W5 未登记声明面（显式豁免）----
if (Test-Path -LiteralPath $workflowsDir) {
    $declared = @(Get-ChildItem -LiteralPath $workflowsDir -Filter '*.ushar' -File |
        ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name) } | Sort-Object -Unique)
    foreach ($b in (Get-SetDiff -Left $declared -Right $constBases)) {
        if ($unregisteredExempt -notcontains $b) {
            $violations.Add("W5 未登记声明面：$b.ushar（三登记集合均缺，且不在豁免基线）")
        }
    }
}

# ---- W6 正/负对照 ----
if ($constBases.Count -lt $minRegistrations) {
    $violations.Add("W6 正对照失效：登记数 $($constBases.Count) < 基线 $minRegistrations")
}
$syntheticDiff = Get-SetDiff -Left @('a', 'b') -Right @('a')
if ($syntheticDiff.Count -ne 1 -or $syntheticDiff[0] -ne 'b') {
    $violations.Add('W6 负对照失效：集合比较函数未报出合成差异样本')
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ("[OK] WorkflowRegistration：0 违规（登记 {0} 条；三集合等价 ∧ 流号在位 ∧ 豁免 {1}）" -f $constBases.Count, ($unregisteredExempt -join ',')) -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] WorkflowRegistration 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：新增流 = WorkflowSpecFiles 常量 + DeclaredFlowIds.Map + WorkflowRegistrationCatalog.All 三处同步。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0
