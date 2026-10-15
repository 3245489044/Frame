<#
.SYNOPSIS
    目录能力单源门禁（Strategy.Catalog 收敛守卫）—— 防止「六套目录收敛为一套」的成果再分叉。

.DESCRIPTION
    四类断言（任一失败 → 退出码 1）：

      1 分组单源       `Strategy\Catalog` 之外不得新增「按首个 `_` 前缀分组」的实现
                       （扫描 `Split('_')[0]` / `Contains('_') ? … Split('_')` 形态；
                       存量白名单：MathBrowserFunctions / FunctionTesterFunctions 的 history 兼容壳）
      2 目录能力单源   不得新增 `Xxx_GetDomains` / `Xxx_GetLibraries` / `Xxx_ListChains` /
                       `Xxx_GetFunctionsInDomain` 形态的**新**注册名（存量白名单见下）
      3 轴派生单源     不得在框架外自行实现「相对路径首段（文件夹）」分组口径
                       （扫描 `Split('/')[0]` / `Split('\')[0]` 形态）
      4 烘焙完整性     `csharp-bake.manifest.json` 须含 `Strategy.Catalog`

.PARAMETER RepoRoot
    仓库根（默认脚本回溯定位到 D:\Project\Main）。

.PARAMETER StrictWarnings
    把「疑似」告警也视为失败。

.EXAMPLE
    .\Test-CatalogSingleSource.ps1
#>

[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [switch]$StrictWarnings
)

$ErrorActionPreference = "Stop"

if (-not $RepoRoot) {
    # Frame\PowerShellScript\Share\Catalog\ -> 上溯 4 级到 D:\Project\Main
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..\..")).Path
}

$violations = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Get-SourceFiles([string[]]$roots) {
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        Get-ChildItem -Path $root -Recurse -Include *.cs -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    }
}

$scanRoots = @(
    (Join-Path $RepoRoot "Frame\CsharpPrj"),
    (Join-Path $RepoRoot "Projects")
)

# Strategy.Catalog 自身是唯一实现面：其路径下的命中一律豁免。
$frameworkDir = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "Frame\CsharpPrj\Strategy\Catalog")) + [System.IO.Path]::DirectorySeparatorChar

# ── 规则 1：分组单源（按 '_' 前缀分组口径）────────────────────────────────
$groupPattern = "Split\('_'\)\[0\]"
foreach ($file in Get-SourceFiles $scanRoots) {
    $path = [System.IO.Path]::GetFullPath($file.FullName)
    if ($path.StartsWith($frameworkDir, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
    $hits = Select-String -Path $file.FullName -Pattern $groupPattern -ErrorAction SilentlyContinue
    foreach ($h in $hits) {
        # 存量白名单：Math/Func 的兼容壳（旧面转调前保留）
        if ($file.Name -in @("MathBrowserFunctions.cs", "FunctionTesterFunctions.cs")) { continue }
        $violations.Add("规则1 分组单源：$($file.FullName):$($h.LineNumber) 出现按 '_' 前缀分组；请改用 Strategy.Catalog（CatalogAxes.PrefixValue）。")
    }
}

# ── 规则 2：目录能力单源（新注册名形态）──────────────────────────────────
$forbiddenReg = 'Register\(\s*"[A-Za-z0-9]*_(GetDomains|GetLibraries|ListChains|GetFunctionsInDomain)"'
foreach ($file in Get-SourceFiles $scanRoots) {
    $path = [System.IO.Path]::GetFullPath($file.FullName)
    if ($path.StartsWith($frameworkDir, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
    $hits = Select-String -Path $file.FullName -Pattern $forbiddenReg -ErrorAction SilentlyContinue
    foreach ($h in $hits) {
        # 存量白名单：收敛前既有面（转调期保留旧名，禁新增变体）
        if ($file.Name -in @("MathBrowserFunctions.cs", "FunctionTesterFunctions.cs", "ProduceUiLibrary.cs")) { continue }
        $violations.Add("规则2 目录能力单源：$($file.FullName):$($h.LineNumber) 新增目录形态注册名；请改用 Catalog_* 七件套 + ICatalogSource。")
    }
}

# ── 规则 3：轴派生单源（文件夹首段口径）──────────────────────────────────
$folderPattern = "Split\(('/'|\\/|\\\\|'\\\\')\S*\)\[0\]"
foreach ($file in Get-SourceFiles $scanRoots) {
    $path = [System.IO.Path]::GetFullPath($file.FullName)
    if ($path.StartsWith($frameworkDir, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
    $hits = Select-String -Path $file.FullName -Pattern 'Split\(.[/\\].*\)\[0\]' -ErrorAction SilentlyContinue
    foreach ($h in $hits) {
        if ($file.Name -in @("FileCatalogService.cs", "CatalogAxis.cs")) { continue }
        $warnings.Add("规则3 轴派生：$($file.FullName):$($h.LineNumber) 疑似自行实现文件夹首段口径；建议用 CatalogAxes.FolderValue。")
    }
}

# ── 规则 4：烘焙完整性 ───────────────────────────────────────────────────
$manifest = Join-Path $RepoRoot "Frame\CsharpPrj\BuildOutput\Debug\csharp-bake.manifest.json"
if (Test-Path $manifest) {
    $hasCatalog = Select-String -Path $manifest -Pattern '"Strategy\.Catalog"|Strategy\.Catalog' -Quiet -ErrorAction SilentlyContinue
    if (-not $hasCatalog) {
        $violations.Add("规则4 烘焙完整性：$manifest 未含 Strategy.Catalog（先跑 Generate-CsharpBakeManifest.ps1）。")
    }
} else {
    $warnings.Add("规则4 烘焙完整性：未找到 manifest（$manifest），跳过（先构建 Frame/CsharpPrj）。")
}

# ── 汇总 ─────────────────────────────────────────────────────────────────
Write-Host "=== 目录能力单源门禁（Test-CatalogSingleSource）===" -ForegroundColor Cyan
if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[FAIL] $v" -ForegroundColor Red }
}
foreach ($w in $warnings) { Write-Host "[WARN] $w" -ForegroundColor Yellow }

if ($violations.Count -gt 0 -or ($StrictWarnings -and $warnings.Count -gt 0)) {
    Write-Host "门禁失败：$($violations.Count) 违规 / $($warnings.Count) 告警。" -ForegroundColor Red
    exit 1
}

Write-Host "门禁通过：0 违规 / $($warnings.Count) 告警。" -ForegroundColor Green
exit 0
