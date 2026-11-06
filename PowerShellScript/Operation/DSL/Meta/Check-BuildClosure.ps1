<#
.SYNOPSIS
    构建闭包门禁（Gate=BuildClosure）—— 「启动必需闭包」与装配组声明同源，禁脚本内联产品清单。
.DESCRIPTION
    判据载体 = BuildTargetMap.props 的 BuildProfiles.launch 结构 + assembly-closure.json + 脚本文本结构。
      K1 launch profile 的 target 集合 ⊇ 各 required 装配组的 buildTarget
      K2 禁脚本内联产品清单（Label='Agent|Engine|Produce' 形态）
      K3 required 装配组的清单存在（复用 AssemblyClosure 单一实现，不复制判据）
      K4 正对照：launch target 数 ≥ 基线
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects'))) { $repoRoot = Split-Path -Parent $repoRoot }
if (-not $repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

$modulePath = Join-Path (Get-MappedPath -Key 'PowerShellScript') 'Share\AssemblyClosure\AssemblyClosure.psm1'
$baselinePath = Join-Path (Get-MappedPath -Key 'Baselines') 'Meta\BuildClosure.psd1'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  构建闭包守卫（BuildClosure）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

foreach ($p in @($baselinePath, $modulePath)) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Host "  [FAIL] 前置缺失: $p" -ForegroundColor Red; exit 2 }
}
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
Import-Module $modulePath -Force -DisableNameChecking

$violations = New-Object System.Collections.Generic.List[string]
$profileName = [string]$baseline.ProfileName.Value
$mapPath = Join-Path $repoRoot ([string]$baseline.MapPropsRelative.Value)

if (-not (Test-Path -LiteralPath $mapPath)) {
    Write-Host "  [FAIL] 构建图声明缺失: $mapPath" -ForegroundColor Red
    exit 2
}

# K1：解析 launch profile
$mapText = Get-Content -LiteralPath $mapPath -Raw -Encoding UTF8
$marker = '<DistributionProfile Include="{0}">' -f $profileName
$idx = $mapText.IndexOf($marker)
if ($idx -lt 0) {
    $violations.Add(("[K1] BuildTargetMap.props 缺 BuildProfiles.{0}" -f $profileName))
    $launchTargets = @()
}
else {
    $segment = $mapText.Substring($idx)
    $m = [regex]::Match($segment, '<Targets>(?<v>[^<]*)</Targets>')
    if (-not $m.Success) {
        $violations.Add(("[K1] BuildProfiles.{0} 未声明 <Targets>" -f $profileName))
        $launchTargets = @()
    }
    else {
        $launchTargets = @($m.Groups['v'].Value -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
}

$minTargets = [int]$baseline.MinLaunchTargets.Value
Write-Host ("  {0} profile: {1} 个 target（正对照下限 {2}）" -f $profileName, $launchTargets.Count, $minTargets) -ForegroundColor Gray
if ($launchTargets.Count -lt $minTargets) {
    $violations.Add(("[K4] 闭包坍缩：launch target 数 {0} < 基线 {1}" -f $launchTargets.Count, $minTargets))
}

# K1 续：required 装配组的 buildTarget 必须在 launch 内
$states = @(Get-ClosureStates -RepoRoot $repoRoot -Configuration 'Debug')
foreach ($s in $states) {
    if (-not $s.Required) { continue }
    if ([string]::IsNullOrWhiteSpace($s.BuildTarget)) { continue }
    if ($launchTargets -notcontains $s.BuildTarget) {
        $violations.Add(("[K1] 装配组 {0} 的构建目标 '{1}' 不在 {2} profile" -f $s.Id, $s.BuildTarget, $profileName))
    }
}

# K2：禁脚本内联产品清单（结构事实：文本模式）
$scanDirs = @(
    (Join-Path $repoRoot 'Frame\PowerShellScript\Share\Build'),
    (Join-Path $repoRoot 'EnableMent')
)
$inlinePattern = "Label\s*=\s*'(Agent|Engine|Produce)'"
$inlineKinds = @('Agent', 'Engine', 'Produce')
foreach ($dir in $scanDirs) {
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    foreach ($f in @(Get-ChildItem -LiteralPath $dir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.ps1', '.bat') })) {
        $rel = $f.FullName.Substring($repoRoot.Length).TrimStart('\')
        if (@($baseline.AllowedInlineClosureFiles.Value) -contains $rel) { continue }
        $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
        foreach ($kind in $inlineKinds) {
            if ($text -match ("Label\s*=\s*'" + $kind + "'")) {
                $violations.Add(("[K2] {0}: 内联产品清单（Label='{1}'）⇒ 应读 assembly-closure.json" -f $rel, $kind))
            }
        }
    }
}

# K3：required 组的清单存在（复用闭包实现）
foreach ($s in $states) {
    if (-not $s.Required) { continue }
    if ($s.Missing.Count -gt 0) {
        $violations.Add(("[K3] 装配组 {0} 未就绪：{1}" -f $s.Id, (($s.Missing | Select-Object -First 3) -join '; ')))
    }
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 构建闭包与装配组同源（launch 覆盖齐备 / 无内联清单）' -ForegroundColor Green
exit 0
