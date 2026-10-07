#requires -Version 5.1
<#
.SYNOPSIS
    spec 生成物 / 手写物**分离边界**守卫（SpecGeneratedBoundary）—— R4 SEP 的机器判据。

.DESCRIPTION
    背景（方案 §4.5 R4 / §3.4）：生成面（机器产物）与手写面（真源）此前只有 R0 的「源码树零生成物」一条事实；
    「生成物必有清单且未被手改」「obj 面不出现第二份生成物」仍靠约定。本件把**目录契约**落成可判定判据。

    判据（载体 = 结构事实：文件存在 / 计数 / 内容前缀 / sha256；禁执行者自述）：
      G1 源码树零生成物：扫描面内生成物文件名模式命中数 = 0（手写遗留白名单除外）；
      G2 obj 面无第二生成物：`obj\**\{*SpecTests.g.cs, SpecPorts.g.cs}` 计数 = 0（K2「两处生成物」二选一 = 显式关闭）；
      G3 生成物头注释：生成根内 `.spec.json` / `*.g.cs` 首行须命中基线头前缀（防手造产物混入生成根）；
      G4 源 ⇄ 产物双向：生成根每个 `.spec.json` 的 suiteId 须能反查 `.specs` 真源（孤儿 = 陈旧 / 手造）；
      G5 manifest 一致性（**条件判据**）：`_manifest.json` 存在 → artifacts ⇔ 实际产物双向 ∧ sha256 一致；
         不存在 → 记 ⊥（可见化，不作放行依据；对齐 AX-FRAME.CriterionWellFormedness）；
      U 正 / 负对照：判据函数对合成样本行为正确（防判据被改坏 ⇒ 空转）。

    模式：默认报告模式（不设退出码）；-Check 时违规 → 退出码 1；-Warn 时即使违规也不设退出码（方案 §4.5 回退项）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER Warn
    只报告模式（回退开关）：违规不设退出码。
.PARAMETER ShowDetail
    输出逐条实测落点。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$Warn,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$b = Import-GateBaseline -Name SpecGeneratedBoundary

$violations = [System.Collections.Generic.List[string]]::new()
$pending = [System.Collections.Generic.List[string]]::new()

$scanRoots = @($b.SourceScanRoots.Value)
$objScanRoots = @($b.ObjScanRoots.Value)
$genRoots = @($b.GeneratedRoots.Value)
$genPatterns = @($b.GeneratedPatterns.Value)
$objPatterns = @($b.ObjGeneratedPatterns.Value)
$jsonHeader = [string]$b.SpecJsonHeaderPrefix.Value
$codeHeader = [string]$b.GeneratedCodeHeaderPrefix.Value
$allowLegacy = @($b.HandwrittenLegacyJson.Value)
$minScanRoots = [int]$b.MinScanRoots.Value
$minPatterns = [int]$b.MinGeneratedPatterns.Value

$script:GenPatterns = $genPatterns
$script:ObjPatterns = $objPatterns

# 模式匹配单点（实测与正 / 负对照共用同一函数）。
function Test-GeneratedName {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string[]]$Patterns)
    foreach ($p in $Patterns) { if ($Name -like $p) { return $true } }
    return $false
}

function Test-GenArtifactName { param([Parameter(Mandatory)][string]$Name) return (Test-GeneratedName -Name $Name -Patterns $script:GenPatterns) }
function Test-ObjArtifactName { param([Parameter(Mandatory)][string]$Name) return (Test-GeneratedName -Name $Name -Patterns $script:ObjPatterns) }

function Get-RelPath { param([Parameter(Mandatory)][string]$Full) return $Full.Substring($Root.Length).TrimStart('\') }

$excludeRx = '\\BuildOutput\\|\\obj\\|\\bin\\|\\_deprecated\\'

$existingRoots = @()
foreach ($r in $scanRoots) { if (Test-Path -LiteralPath (Join-Path $Root $r)) { $existingRoots += $r } }

# ---- G1：源码树零生成物（手写遗留白名单除外）----
$hits = [System.Collections.Generic.List[string]]::new()
$legacyHits = 0
foreach ($scanRoot in $existingRoots) {
    $abs = Join-Path $Root $scanRoot
    foreach ($f in @(Get-ChildItem -LiteralPath $abs -Recurse -File -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -notmatch $excludeRx })) {
        if (-not (Test-GenArtifactName -Name $f.Name)) { continue }
        $rel = Get-RelPath -Full $f.FullName
        $isAllowed = $false
        foreach ($a in $allowLegacy) { if ($rel -eq $a) { $isAllowed = $true; break } }
        if ($isAllowed) { $legacyHits++; continue }
        $hits.Add($rel)
    }
}
foreach ($h in $hits) { $violations.Add("[G1] 源码树出现 spec 生成物（应落生成根）：$h") }

# ---- G2：obj 面无第二生成物（K2 两处生成物 = 显式关闭 EmitCompilerGeneratedFiles）----
$objHits = [System.Collections.Generic.List[string]]::new()
foreach ($objRoot in @($objScanRoots)) {
    $abs = Join-Path $Root $objRoot
    if (-not (Test-Path -LiteralPath $abs)) { continue }
    foreach ($f in @(Get-ChildItem -LiteralPath $abs -Recurse -File -Filter '*.g.cs' -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -match '\\obj\\' })) {
        if (-not (Test-ObjArtifactName -Name $f.Name)) { continue }
        $objHits.Add((Get-RelPath -Full $f.FullName))
    }
}
foreach ($h in $objHits) { $violations.Add("[G2] obj 面出现第二生成物（对拍请落临时目录，勿留 obj）：$h") }

# ---- 生成根清点（.spec.json / *.g.cs / _manifest.json）----
$genSpecJson = [System.Collections.Generic.List[object]]::new()
$genCode = [System.Collections.Generic.List[object]]::new()
$genManifests = [System.Collections.Generic.List[object]]::new()
$existingGenRoots = @()
foreach ($r in $genRoots) {
    $abs = Join-Path $Root $r
    if (-not (Test-Path -LiteralPath $abs)) { continue }
    $existingGenRoots += $abs
    foreach ($f in @(Get-ChildItem -LiteralPath $abs -Recurse -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -like '*.spec.json') { $genSpecJson.Add($f) }
        elseif ($f.Name -eq '_manifest.json') { $genManifests.Add($f) }
        elseif ($f.Name -like '*.g.cs') { $genCode.Add($f) }
    }
}

# ---- G3：生成物头注释（防手造产物混入生成根）----
$headerChecked = 0
foreach ($f in $genSpecJson) {
    $headerChecked++
    $first = @(Get-Content -LiteralPath $f.FullName -TotalCount 1 -ErrorAction SilentlyContinue)[0]
    if ([string]::IsNullOrEmpty($first) -or -not $first.StartsWith($jsonHeader, [System.StringComparison]::Ordinal)) {
        $violations.Add("[G3] .spec.json 头注释缺失 / 漂移（首行应以 '$jsonHeader' 开头）：$(Get-RelPath -Full $f.FullName)")
    }
}
foreach ($f in $genCode) {
    $headerChecked++
    $first = @(Get-Content -LiteralPath $f.FullName -TotalCount 1 -ErrorAction SilentlyContinue)[0]
    if ([string]::IsNullOrEmpty($first) -or -not $first.StartsWith($codeHeader, [System.StringComparison]::Ordinal)) {
        $violations.Add("[G3] 生成码头注释缺失 / 漂移（首行应以 '$codeHeader' 开头）：$(Get-RelPath -Full $f.FullName)")
    }
}

# ---- G4：源 ⇄ 产物双向（孤儿产物 = 陈旧 / 手造）----
$sourceSuites = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sourceSpecsCount = 0
foreach ($scanRoot in $existingRoots) {
    $abs = Join-Path $Root $scanRoot
    foreach ($f in @(Get-ChildItem -LiteralPath $abs -Recurse -File -Filter '*.specs' -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -notmatch $excludeRx })) {
        $sourceSpecsCount++
        [void]$sourceSuites.Add([System.IO.Path]::GetFileNameWithoutExtension($f.Name))
    }
}
$orphans = 0
foreach ($f in $genSpecJson) {
    $suiteId = $f.Name.Substring(0, $f.Name.Length - '.spec.json'.Length)
    if (-not $sourceSuites.Contains($suiteId)) {
        $orphans++
        $violations.Add("[G4] 孤儿产物（生成根无对应 .specs 真源）：$(Get-RelPath -Full $f.FullName)")
    }
}

# ---- G5：manifest 一致性（条件判据；条件不满足 → ⊥ 可见化）----
$manifestChecked = 0
foreach ($m in $genManifests) {
    $manifestChecked++
    $data = Get-Content -LiteralPath $m.FullName -Raw | ConvertFrom-Json
    $baseDir = Split-Path -Parent $m.FullName
    $genRootOfManifest = Split-Path -Parent $baseDir
    $listed = @{}
    foreach ($a in @($data.artifacts)) {
        $rel = ([string]$a.path).Replace('/', '\')
        $full = $null
        if ([System.IO.Path]::IsPathRooted($rel)) { $full = $rel }
        else {
            $candidate = Join-Path $baseDir $rel
            if (Test-Path -LiteralPath $candidate) { $full = (Resolve-Path -LiteralPath $candidate).Path }
            else {
                # R1 清单 path 相对「消费方工程目录」；本件按 `SpecGen\<工程>\` 锚点补解析（路径口径兼容）。
                $anchor = '\' + [string]$data.project + '\'
                $idx = $rel.LastIndexOf($anchor, [System.StringComparison]::Ordinal)
                if ($idx -ge 0) { $full = Join-Path $genRootOfManifest $rel.Substring($idx + 1) }
            }
        }
        if (-not $full -or -not (Test-Path -LiteralPath $full)) {
            $violations.Add("[G5] 清单产物不存在 / 路径不可解析：${rel}（清单 $($m.Name)）")
            continue
        }
        $listed[$full] = $true
        $actual = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$a.sha256).ToLowerInvariant()) {
            $violations.Add("[G5] 生成物被手改 / 漂移（sha256 不一致）：$rel")
        }
    }
    foreach ($f in @(Get-ChildItem -LiteralPath $baseDir -Recurse -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -eq '_manifest.json') { continue }
        if (-not $listed.ContainsKey($f.FullName)) {
            $violations.Add("[G5] 生成根出现清单未登记产物（清点不一致）：$(Get-RelPath -Full $f.FullName)")
        }
    }
}
if ($genManifests.Count -eq 0) {
    $pending.Add('[G5][⊥] 生成根无 _manifest.json —— 清单 / 手改侧不可判定（经 Frame\Invoke\Invoke-SpecGen.ps1 -Manifest 生成后再判）')
}

# ---- 判据空转防护 ----
if ($existingRoots.Count -lt $minScanRoots) {
    $violations.Add("[U] 扫描面 $($existingRoots.Count) 根 < 下限 $minScanRoots（判据无标的假绿）")
}
if (@($genPatterns).Count -lt $minPatterns) {
    $violations.Add("[U] 生成物模式数 $(@($genPatterns).Count) < 下限 $minPatterns（判据无标的假绿）")
}
if ($existingGenRoots.Count -gt 0 -and $sourceSpecsCount -eq 0) {
    $violations.Add('[U] 生成根非空但源码面无 .specs（G4 无标的，禁假绿）')
}

# ---- U 正 / 负对照（判据自证）----
$posOk = (Test-GenArtifactName -Name 'core.x.spec.json') -and
         (Test-GenArtifactName -Name 'CoreXSpecTests.g.cs') -and
         (Test-ObjArtifactName -Name 'SpecPorts.g.cs')
$negOk = (-not (Test-GenArtifactName -Name 'core.x.specs')) -and
         (-not (Test-ObjArtifactName -Name 'Agent.Core.Tests.GlobalUsings.g.cs')) -and
         (-not (Test-GenArtifactName -Name 'FooTests.cs'))
if (-not $posOk) { $violations.Add('[U] 正对照失败：合成生成物样本未被捕获（判据函数被改坏）') }
if (-not $negOk) { $violations.Add('[U] 负对照失败：非生成物样本被误捕') }

$hdrPos = '// 生成物，勿手改：由 Cases\x\x.specs 展开。'
$hdrNeg = '{ "suite": "x" }'
if (-not $hdrPos.StartsWith($jsonHeader, [System.StringComparison]::Ordinal)) { $violations.Add('[U] 正对照失败：头注释前缀规则未被识别') }
if ($hdrNeg.StartsWith($jsonHeader, [System.StringComparison]::Ordinal)) { $violations.Add('[U] 负对照失败：无头注释样本被误判') }

# ---- 报告 ----
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  spec 生成 / 手写分离边界守卫（SpecGeneratedBoundary）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host "  扫描面：$($existingRoots.Count) 根 · 源码 .specs $sourceSpecsCount 件" -ForegroundColor Gray
Write-Host "  生成根：$($existingGenRoots.Count) 个 · .spec.json $($genSpecJson.Count) 件 · *.g.cs $($genCode.Count) 件 · manifest $($genManifests.Count) 件" -ForegroundColor Gray
Write-Host "  源码树生成物：$($hits.Count) 处（手写遗留白名单 $legacyHits 处）；obj 第二生成物：$($objHits.Count) 处；孤儿产物：$orphans 处" -ForegroundColor Gray
Write-Host "  头注释核对：$headerChecked 件；manifest 核对：$manifestChecked 件" -ForegroundColor Gray

if ($ShowDetail) {
    if ($hits.Count -gt 0) {
        Write-Host '  --- 源码树违规落点 ---' -ForegroundColor DarkGray
        foreach ($h in $hits) { Write-Host "    - $h" -ForegroundColor DarkGray }
    }
    if ($objHits.Count -gt 0) {
        Write-Host '  --- obj 第二生成物落点 ---' -ForegroundColor DarkGray
        foreach ($h in $objHits) { Write-Host "    - $h" -ForegroundColor DarkGray }
    }
}

if ($pending.Count -gt 0) {
    Write-Host ''
    foreach ($p in $pending) { Write-Host "  $p" -ForegroundColor DarkYellow }
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] SpecGeneratedBoundary：0 违规（源码树零生成物 / obj 零残留 / 头注释齐备 / 无孤儿产物 / 正负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] SpecGeneratedBoundary 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Warn) { exit 0 }
if ($Check) { exit 1 }
exit 0
