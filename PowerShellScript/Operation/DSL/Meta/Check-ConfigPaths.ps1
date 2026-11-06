#requires -Version 5.1
<#
.SYNOPSIS
    配置路径防回潮守卫（ConfigPaths）—— 声明路径只允许出现在 ConfigPathCatalog（单一登记表）。

.DESCRIPTION
    判据：
      P1 C# 消费代码禁字面量：`@"Config\..."` / `"Config\\..."` / `Path.Combine("Config", ...)`
         （白名单：ConfigPathCatalog.cs / 测试 / 生成物 / 本门禁自身）。
      P2 PowerShell 消费面（Projects\Plug\Agent\PowerShellScript）禁 `Join-Path ... 'Config\...'`
         与 `'Projects\Plug\Agent\Config\...'`。
      P3 棘轮：基线 DebtAllowlist 逐文件 MaxHits；未登记的新硬编码 = 红；超基线 = 红；条目归零未删 = 红。
      P4 正/负对照：合成样本（@"Config\x.ushar"、Path.Combine("Config",...)）必须被判红，干净样本不判红。

    模式：默认报告；-Check 违规 → 退出码 1；-ShowActual 输出当前命中清单（供维护基线）。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$ShowActual
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects')))) {
    $repoRoot = Split-Path -Parent $repoRoot
}
if (-not $repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }

Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name ConfigPaths
$debt = @($baseline.DebtAllowlist.Value)
$allowed = @($baseline.AllowedPathPatterns.Value)

$violations = [System.Collections.Generic.List[string]]::new()

function Test-AllowedPath {
    param([string]$RelativePath)
    foreach ($pattern in $allowed) {
        if ($RelativePath -match $pattern) { return $true }
    }
    return $false
}

function Get-ConfigPathHits {
    param([string]$Text, [switch]$PowerShell)

    $clean = [regex]::Replace($Text, '(?s)/\*.*?\*/', '')
    $lines = @($clean -split "`r?`n")
    $kept = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        $trim = $line.TrimStart()
        if ($trim.StartsWith('//')) { continue }
        if ($PowerShell -and $trim.StartsWith('#')) { continue }
        $kept.Add($line)
    }
    $body = ($kept -join "`n")

    $patterns = if ($PowerShell) {
        @(
            'Join-Path[^\r\n]*[''"]Config[\\/]',
            '[''"]Projects\\Plug\\Agent\\Config[\\/]'
        )
    }
    else {
        @(
            '"[^"\r\n]*(?<![A-Za-z0-9_])Config\\{1,2}',
            'Path\.Combine\(\s*"Config"\s*,'
        )
    }

    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $patterns) {
        foreach ($m in [regex]::Matches($body, $p)) {
            $hits.Add($m.Value.Trim())
        }
    }
    return $hits
}

# ---- 扫描面 ----
$scanFiles = [System.Collections.Generic.List[object]]::new()
$csharpRoot = Join-Path $repoRoot 'Projects\Plug\Agent\src'
if (Test-Path -LiteralPath $csharpRoot) {
    foreach ($f in (Get-ChildItem -LiteralPath $csharpRoot -Recurse -Filter '*.cs' -File)) {
        $rel = [IO.Path]::GetRelativePath($repoRoot, $f.FullName).Replace('\', '/')
        if ($rel -match '/(obj|bin)/' -or $rel -match '\.Tests/' -or $f.Name -match '\.g\.(cs|i\.cs)$') { continue }
        $scanFiles.Add([pscustomobject]@{ Path = $f.FullName; Relative = $rel; PowerShell = $false })
    }
}
$agentScriptRoot = Join-Path $repoRoot 'Projects\Plug\Agent\PowerShellScript'
if (Test-Path -LiteralPath $agentScriptRoot) {
    foreach ($f in (Get-ChildItem -LiteralPath $agentScriptRoot -Recurse -Include '*.ps1', '*.psm1' -File)) {
        $rel = [IO.Path]::GetRelativePath($repoRoot, $f.FullName).Replace('\', '/')
        if ($rel -match '/(obj|bin)/') { continue }
        $scanFiles.Add([pscustomobject]@{ Path = $f.FullName; Relative = $rel; PowerShell = $true })
    }
}

$actual = [System.Collections.Generic.List[object]]::new()
foreach ($file in $scanFiles) {
    if (Test-AllowedPath -RelativePath $file.Relative) { continue }
    $text = Get-Content -LiteralPath $file.Path -Raw -Encoding UTF8
    $hits = @(Get-ConfigPathHits -Text $text -PowerShell:$file.PowerShell)
    if ($hits.Count -gt 0) {
        $actual.Add([pscustomobject]@{ File = $file.Relative; Hits = $hits.Count; Samples = @($hits | Select-Object -Unique -First 2) })
    }
}

if ($ShowActual) {
    $actual | ConvertTo-Json -Depth 4
}

$debtByFile = @{}
foreach ($entry in $debt) {
    $key = ([string]$entry.File).Replace('\', '/')
    $debtByFile[$key] = [int]$entry.MaxHits
}

$actualByFile = @{}
foreach ($a in $actual) { $actualByFile[$a.File] = $a }

foreach ($a in $actual) {
    if (-not $debtByFile.ContainsKey($a.File)) {
        $violations.Add("P3 新增硬编码配置路径：$($a.File)（hits=$($a.Hits)，样例：$($a.Samples -join ' | ')）—— 必须改走 ConfigPathCatalog")
        continue
    }
    if ($a.Hits -gt $debtByFile[$a.File]) {
        $violations.Add("P3 超基线：$($a.File) hits=$($a.Hits) > MaxHits=$($debtByFile[$a.File])")
    }
}
foreach ($file in $debtByFile.Keys) {
    if (-not $actualByFile.ContainsKey($file)) {
        $violations.Add("P3 基线失效：$file 已无命中，请从 DebtAllowlist 移除（棘轮只降不升）")
    }
}


# ---- P4 正/负对照 ----
$ncBad = @(
    (Get-ConfigPathHits -Text 'var p = @"Config\x.ushar";'),
    (Get-ConfigPathHits -Text 'var p = Path.Combine("Config", "x.ushar");')
)
if (($ncBad[0].Count + $ncBad[1].Count) -lt 2) {
    $violations.Add('P4 负对照失效：合成硬编码样本未被判红')
}
$ncGood = @(Get-ConfigPathHits -Text 'var p = ConfigPathCatalog.Relative(ConfigPathCatalog.Ids.ModelGraph);')
if ($ncGood.Count -ne 0) {
    $violations.Add('P4 正对照失效：合规取数样本被误判')
}
$ncBadPs = @(Get-ConfigPathHits -Text "Join-Path `$Root 'Projects\Plug\Agent\Config\nodes'" -PowerShell)
if ($ncBadPs.Count -eq 0) {
    $violations.Add('P4 负对照失效：PS 合成样本未被判红')
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ("[OK] ConfigPaths：0 违规（扫描 {0} 文件；债务文件 {1} 个；新增硬编码 0）" -f $scanFiles.Count, $debtByFile.Count) -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] ConfigPaths 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host '  修复口径：声明路径只允许 ConfigPathCatalog（逻辑 id → 候选路径）；PS 侧经 DeclarationPathResolver（角色 + basename）。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0

