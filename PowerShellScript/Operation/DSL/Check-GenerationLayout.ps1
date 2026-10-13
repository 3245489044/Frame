# Check-GenerationLayout.ps1
# VM-R1 · 代目录布局守卫 —— 校验 <GenRoot>\gen-<n> 的产物契约（对齐
# Docs\analysis\2026\10\10.8\2026-10-09-Vm框架-换代路线收敛-构建到新目录-换ALC-重装配.md §4.1/§8-1/§8-2）。
#
# 判据（逐代目录）：
#   [L1] 目录名格式 gen-<n>（n ≥ 1）
#   [L2] 含 Agent.*.dll，且计数 ≥ 就地产物面参照计数（覆盖同一集合）
#   [L3] 含 Agent.Launcher.deps.json（AssemblyDependencyResolver 基座 = 本目录）
#   [L4] 含 vm-program.json（VM 清单）
#   [L5] current 指针（存在时）须指向合法 gen-<n> 且该代目录在位（生效代单源；缺指针 = 装载面回落最高代）
# 说明：落点**禁** BuildOutput\<Config>（被运行进程锁定）；本脚本只读校验，不构建。
#
#   - 报告模式（默认）：列出各代目录事实与命中，不设退出码。
#   - 校验模式（-Check）：违规 > 0 → 退出码 1（CI 门禁）。
#
# 用法：pwsh Frame\PowerShellScript\Operation\DSL\Check-GenerationLayout.ps1 [-Root <repo>] [-GenRoot <path>] [-Check]

[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$GenRoot = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$agentRoot = Join-Path $Root 'Projects\Plug\Agent'
$inPlace = Join-Path $agentRoot 'BuildOutput\Debug'
if ([string]::IsNullOrWhiteSpace($GenRoot)) {
    $GenRoot = Join-Path $agentRoot 'BuildOutput\Generations'
}

$referenceCount = 0
if (Test-Path $inPlace) {
    $referenceCount = @(Get-ChildItem $inPlace -Filter 'Agent.*.dll' -File -ErrorAction SilentlyContinue).Count
}

$violations = [System.Collections.Generic.List[string]]::new()
$gens = @()

Write-Host '=== [VM-R1] 代目录布局契约（gen-<n> 产物面） ==='
Write-Host ("代根: {0}" -f $GenRoot)
Write-Host ("就地参照: Agent dll {0} 件（{1}）" -f $referenceCount, $inPlace)

if (-not (Test-Path $GenRoot)) {
    Write-Host '[INFO] 代根不存在（尚无构件代产出）——判据退化为未接线（勿视为 PASS）。'
    if ($Check) { exit 1 }
    exit 0
}

$dirs = @(Get-ChildItem $GenRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
if ($dirs.Count -eq 0) {
    Write-Host '[INFO] 代根为空（尚无构件代）。'
    if ($Check) { exit 1 }
    exit 0
}

foreach ($dir in $dirs) {
    if ($dir.Name -notmatch '^gen-([1-9]\d*)$') {
        $violations.Add(("[L1] 目录名非 gen-<n>：{0}" -f $dir.Name))
        continue
    }

    $dlls = @(Get-ChildItem $dir.FullName -Filter 'Agent.*.dll' -File -ErrorAction SilentlyContinue)
    $hasDeps = Test-Path (Join-Path $dir.FullName 'Agent.Launcher.deps.json')
    $hasManifest = Test-Path (Join-Path $dir.FullName 'vm-program.json')

    $gens += [pscustomobject]@{ Gen = $dir.Name; Dlls = $dlls.Count; Deps = $hasDeps; Manifest = $hasManifest }

    if ($dlls.Count -eq 0) { $violations.Add(("[L2] {0}：无 Agent.*.dll" -f $dir.Name)) }
    elseif ($referenceCount -gt 0 -and $dlls.Count -lt $referenceCount) {
        $violations.Add(("[L2] {0}：Agent dll 计数 {1} < 参照 {2}" -f $dir.Name, $dlls.Count, $referenceCount))
    }
    if (-not $hasDeps)     { $violations.Add(("[L3] {0}：缺 Agent.Launcher.deps.json" -f $dir.Name)) }
    if (-not $hasManifest) { $violations.Add(("[L4] {0}：缺 vm-program.json" -f $dir.Name)) }
}

foreach ($g in $gens) {
    Write-Host ("  {0}: Agent dll {1} | deps {2} | manifest {3}" -f $g.Gen, $g.Dlls, $g.Deps, $g.Manifest)
}

# [L5] 生效代指针（V1 单源）：存在则须指向合法 gen-<n> 且该代目录在位。
$pointerPath = Join-Path $GenRoot 'current'
if (Test-Path -LiteralPath $pointerPath) {
    $pointerName = (Get-Content -LiteralPath $pointerPath -Raw -ErrorAction SilentlyContinue).Trim()
    if ($pointerName -notmatch '^gen-[1-9]\d*$') {
        $violations.Add(("[L5] current 指针内容非法：'{0}'（应形如 gen-<n>）" -f $pointerName))
    } elseif (-not (Test-Path -LiteralPath (Join-Path $GenRoot $pointerName))) {
        $violations.Add(("[L5] current 指针指向不存在代目录：{0}" -f $pointerName))
    } else {
        Write-Host ("  current 指针: {0}" -f $pointerName)
    }
} else {
    Write-Host '  current 指针: (缺 —— 装载面回落「最高代」枚举；建议经 Build-AgentGeneration.ps1 产出以固化生效代)'
}

Write-Host ("代目录数: {0} | 违规命中: {1}" -f $gens.Count, $violations.Count)
if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---'
    $violations | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
}

if ($Check -and $violations.Count -gt 0) {
    Write-Host '校验失败：代目录布局契约未满足（gen-<n> 须含全 Agent dll + deps.json + vm-program.json）。'
    exit 1
}
exit 0
