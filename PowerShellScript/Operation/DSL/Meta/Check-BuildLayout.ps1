<#
.SYNOPSIS
    产物落点门禁（Gate=BuildLayout）—— 落点必须带配置层；自包含 props 必须 Import 生成件；禁扁平权威层。
.DESCRIPTION
    判据载体 = props 文本结构 + 文件系统结构（禁执行者叙述）。
      L1 任何 <OutputPath> 取值须含 $(Configuration)（空配置 ⇒ 扁平 BuildOutput，事故形态）
      L2 自包含 props 须 Import Frame\generated\BuildLayout.generated.props
      L3 扁平权威层文件数 = 0（由装配组根派生父目录扫描，不手抄产品清单）
      L4 正对照：扫描 props 数 ≥ 基线（防解析面坍缩 ⇒ 假绿）
    退出码：0 无违规 / 1 违规 / 2 前置缺失。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'
# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects')))) { $repoRoot = Split-Path -Parent $repoRoot }
if (-not $repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

$modulePath = Join-Path (Get-MappedPath -Key 'PowerShellScript') 'Share\AssemblyClosure\AssemblyClosure.psm1'
$baselinePath = Join-Path (Get-MappedPath -Key 'Baselines') 'Meta\BuildLayout.psd1'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  产物落点守卫（BuildLayout）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

foreach ($p in @($baselinePath, $modulePath)) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Host "  [FAIL] 前置缺失: $p" -ForegroundColor Red; exit 2 }
}
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
Import-Module $modulePath -Force -DisableNameChecking

$violations = New-Object System.Collections.Generic.List[string]

# L1 / L4：扫描 Directory.Build.props
$scanRoots = @((Join-Path $repoRoot 'Frame'), (Join-Path $repoRoot 'Projects'))
$props = @(Get-ChildItem -Path $scanRoots -Filter 'Directory.Build.props' -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(BuildOutput|obj|bin|_deprecated|\.git)\\' })

$minScan = [int]$baseline.MinPropsScanned.Value
Write-Host ("  扫描面: {0} 个 Directory.Build.props（正对照下限 {1}）" -f $props.Count, $minScan) -ForegroundColor Gray
if ($props.Count -lt $minScan) {
    $violations.Add(("[L4] 正对照失效：props 扫描数 {0} < 基线 {1}（禁判绿）" -f $props.Count, $minScan))
}

foreach ($f in $props) {
    $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
    $rel = $f.FullName.Substring($repoRoot.Length).TrimStart('\')
    foreach ($m in [regex]::Matches($text, '<OutputPath>(?<v>[^<]*)</OutputPath>')) {
        $value = $m.Groups['v'].Value
        if ([string]::IsNullOrWhiteSpace($value)) { $violations.Add(("[L1] {0}: <OutputPath> 为空" -f $rel)); continue }
        # 允许 $(变量) 一层（至多 3 跳）间接：变量定义体必须含配置层（禁只看字面量 ⇒ 误判）。
        $resolved = $value
        for ($d = 0; $d -lt 3; $d++) {
            if ($resolved -match '^\$\((?<n>[A-Za-z0-9_]+)\)$') {
                $varName = $Matches['n']
                $def = [regex]::Match($text, ('<' + $varName + '>(?<d>[^<]*)</' + $varName + '>'))
                if (-not $def.Success) { break }
                $resolved = $def.Groups['d'].Value
            }
            else { break }
        }
        if ($resolved -notmatch '\$\(Configuration\)') {
            $violations.Add(("[L1] {0}: <OutputPath>={1} → {2} 缺配置层（空配置 ⇒ 扁平 BuildOutput）" -f $rel, $value, $resolved))
        }
    }
}

# L2：自包含 props 必须 Import 生成件
foreach ($rel in @($baseline.SelfContainedProps.Value)) {
    $full = Join-Path $repoRoot $rel
    if (-not (Test-Path -LiteralPath $full)) { $violations.Add(("[L2] 自包含 props 缺失: {0}" -f $rel)); continue }
    $text = Get-Content -LiteralPath $full -Raw -Encoding UTF8
    if ($text -notmatch 'BuildLayout\.generated\.props') {
        $violations.Add(("[L2] {0}: 未 Import 生成件（求值期 Configuration 可能为空）" -f $rel))
    }
}

# L3：扁平权威层（由装配组根派生父目录，不手抄产品清单）
$allowFlat = [int]$baseline.AllowFlatAuthorityFiles.Value
try {
    $states = @(Get-ClosureStates -RepoRoot $repoRoot -Configuration 'Debug')
    $seen = @{}
    foreach ($s in $states) {
        $parent = Split-Path -Parent $s.Root
        if (-not $parent -or $seen.ContainsKey($parent)) { continue }
        $seen[$parent] = $true
        if (-not (Test-Path -LiteralPath $parent)) { continue }
        $flat = @(Get-ChildItem -LiteralPath $parent -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in @('.dll', '.exe', '.deps.json') })
        if ($flat.Count -gt $allowFlat) {
            $violations.Add(("[L3] 扁平权威层 {0}: {1} 个文件（禁第二形态；应为 <BuildOutput>\<Config>\）" -f $parent, $flat.Count))
            foreach ($x in ($flat | Select-Object -First 5)) { Write-Host ("         {0}" -f $x.Name) -ForegroundColor DarkGray }
        }
    }
}
catch {
    $violations.Add(("[L3] 扁平层判定不可得: {0}" -f $_.Exception.Message))
}

Write-Host ''
if ($violations.Count -gt 0) {
    Write-Host ("  [FAIL] 违规 {0} 项" -f $violations.Count) -ForegroundColor Red
    foreach ($v in $violations) { Write-Host ("         {0}" -f $v) -ForegroundColor DarkRed }
    if ($Check) { exit 1 }
    exit 0
}
Write-Host '  [OK] 落点单源（配置层齐备 / 生成件已 Import / 无扁平权威层）' -ForegroundColor Green
exit 0
