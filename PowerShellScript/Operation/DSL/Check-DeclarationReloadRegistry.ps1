#requires -Version 5.1
<#
.SYNOPSIS
    声明热装载注册完整性守卫（DeclarationReloadRegistry）。

.DESCRIPTION
    沿《任务工作流化-...-方案》与「磁盘变更触发内存变更」要求：进程级声明缓存装载器必须注册进
    DeclarationReloadFramework（参与者注册表），否则其变更不生效（框架维护）。

    判据：
      D1 框架在册：DeclarationReloadFramework.cs 存在且含 ReloadAll
      D2 注册完备：每个暴露 `public static void Reset()` 的装载器类名，必须出现在某 *DeclarationReloadRegistrations.cs 中
      D3 正对照：装载器（带 Reset）数 ≥ 1（防扫描失效假绿）
      D4 负对照：合成「未注册参与者」须被 D2 判据命中
    模式：默认报告模式；-Check 校验模式（违规 → 退出码 1）。
.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
$exclude = 'BuildOutput|\\obj\\|\\docs\\'

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  声明热装载注册完整性守卫（DeclarationReloadFramework）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---- D1 框架在册 ----
$frameworkFile = Get-ChildItem -LiteralPath $agentSrc -Recurse -Filter 'DeclarationReloadFramework.cs' -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $frameworkFile) {
    $violations.Add('[D1] 未找到 DeclarationReloadFramework.cs')
}
elseif ((Get-Content -LiteralPath $frameworkFile.FullName -Raw) -notmatch 'ReloadAll') {
    $violations.Add('[D1] DeclarationReloadFramework 缺 ReloadAll')
}

# ---- 收集：带 public static void Reset() 的声明缓存装载器类名（域 = *SpecLoader.cs）----
$loaderFiles = Get-ChildItem -LiteralPath $agentSrc -Recurse -Filter '*SpecLoader.cs' -File -ErrorAction SilentlyContinue | Where-Object {
    $_.FullName -notmatch $exclude -and (Get-Content -LiteralPath $_.FullName -Raw) -match 'public static void Reset\('
}

function Get-ResetLoaderNames {
    param([object[]]$Files)
    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $Files) {
        $text = Get-Content -LiteralPath $f.FullName -Raw
        foreach ($m in [regex]::Matches($text, 'public static class\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)')) {
            if ($text -match "public static void Reset\(") { $names.Add($m.Groups['n'].Value) }
        }
    }
    return $names
}

$loaderNames = Get-ResetLoaderNames -Files $loaderFiles | Select-Object -Unique

# ---- 注册文件合并文本 ----
$regFiles = Get-ChildItem -LiteralPath $agentSrc -Recurse -Filter '*DeclarationReloadRegistrations.cs' -File -ErrorAction SilentlyContinue
$regText = ($regFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"

# D2 判据（正/负对照共用）
function Test-Registered {
    param([string]$Name, [string]$RegistryText)
    return ($RegistryText -match [regex]::Escape("nameof($Name)") -or $RegistryText -match ('"' + [regex]::Escape($Name) + '"'))
}

# ---- D2 注册完备 ----
$unregistered = [System.Collections.Generic.List[string]]::new()
foreach ($name in $loaderNames) {
    if (-not (Test-Registered -Name $name -RegistryText $regText)) {
        $unregistered.Add($name)
        $violations.Add("[D2] 缓存装载器 $name 未注册进 DeclarationReloadFramework（其变更不会生效）")
    }
}

# ---- D3 正对照 ----
if (@($loaderNames).Count -lt 1) {
    $violations.Add('[D3] 正对照失效：未扫描到任何带 Reset 的装载器（扫描面失效？）')
}

# ---- D4 负对照 ----
if (Test-Registered -Name 'SyntheticUnregisteredLoader' -RegistryText $regText) {
    $violations.Add('[D4] 负对照失效：合成未注册名被误判为已注册（判据恒真？）')
}

Write-Host ("  实测：装载器 {0} 个 / 注册文件 {1} 个 / 未注册 {2} 个" -f (@($loaderNames).Count), (@($regFiles).Count), $unregistered.Count) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '[OK] 声明热装载注册完整性：0 违规（D1 框架在册；D2 注册完备；D3/D4 对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 声明热装载注册完整性命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
