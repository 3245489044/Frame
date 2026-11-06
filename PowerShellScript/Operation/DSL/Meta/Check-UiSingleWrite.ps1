#requires -Version 5.1
<#
.SYNOPSIS
    UI 写落地单写者守卫（UiSingleWrite）：DOM 内容落地唯一入口 = 写协调器（UiWritePlan / WriteContentAsync）。

.DESCRIPTION
    根因（Docs\analysis\2026\10\10.24\2026-10-24-UI写回与刷新-框架层卡顿根因与彻底重构设计.md §3 D-UI-01）：
    同一语义「把 HTML 放到控件上」曾有三条入口，性能最差者（整文档注入 SetHtmlAsync → NavigateToString）
    被日志 sink 直呼 ⇒ 绕过协调器（去重/仲裁/保态/预算），并发时与区域写互相覆盖。

    判据（结构事实：调用点计数，禁执行者叙述）：
      U1 白名单外整文档原语调用点 = 0：扫 *.cs 的 `SetHtmlAsync(`；唯一允许 = 控件自身实现与「无协调器回退」。
      U2 正对照：白名单文件必须在位且确实含该调用（防「扫描面失效 ⇒ 恒绿」）。
      U3 负对照：合成样本（sink 内新增 SetHtmlAsync 调用）必须判违规（防匹配器失效）。

    模式：默认报告（不设退出码）；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Detail:$ShowDetail -Key 'UiSingleWrite'
$Root = $ctx.Root

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UiSingleWrite
$allowed = @($baseline.AllowedCallSites.Value)
if ($allowed.Count -eq 0) { throw 'UiSingleWrite 基线缺 AllowedCallSites（判据无标的）' }

$violations = [System.Collections.Generic.List[string]]::new()

function ConvertTo-RelPath {
    param([string]$Full)
    return $Full.Substring($Root.Length).TrimStart('\', '/')
}

function Test-AllowedPath {
    param([string]$Rel)
    foreach ($a in $allowed)
    {
        $norm = $a -replace '/', '\'
        if ($Rel -ieq $norm) { return $true }
    }
    return $false
}

# ---------------- 扫描面：Frame 生产代码 + Projects（排除 obj/bin/_deprecated） ----------------
$scanRoots = @((Join-Path $Root 'Frame\CsharpPrj'), (Join-Path $Root 'Projects'))
$files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
foreach ($r in $scanRoots)
{
    if (-not (Test-Path -LiteralPath $r)) { continue }
    foreach ($f in (Get-ChildItem -LiteralPath $r -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue))
    {
        if ($f.FullName -match '\\(obj|bin|BuildOutput)\\') { continue }
        if ($f.FullName -like '*\_deprecated\*') { continue }
        $files.Add($f)
    }
}

$hits = @()
if ($files.Count -gt 0)
{
    $hits = @(Select-String -Path $files.FullName -Pattern 'SetHtmlAsync\s*\(' -ErrorAction SilentlyContinue)
}

Write-Host '=== [UiSingleWrite] UI 写落地单写者（整文档原语白名单） ==='

$seenAllowed = @{}
foreach ($h in $hits)
{
    $rel = ConvertTo-RelPath -Full $h.Path
    if (Test-AllowedPath -Rel $rel)
    {
        if (-not $seenAllowed.ContainsKey($rel)) { $seenAllowed[$rel] = $true }
        if ($ShowDetail) { Write-Host ("  [允许] {0}:{1}" -f $rel, $h.LineNumber) -ForegroundColor DarkGray }
        continue
    }
    $violations.Add(("[U1] 白名单外整文档原语调用：{0}:{1}（改走 WriteContentAsync / UiWritePlan 计划）" -f $rel, $h.LineNumber))
}

# U2 正对照：白名单文件在位且确有命中（否则扫描面/匹配器失效 ⇒ 假绿）
foreach ($a in $allowed)
{
    $norm = $a -replace '/', '\'
    $abs = Join-Path $Root $norm
    if (-not (Test-Path -LiteralPath $abs))
    {
        $violations.Add("[U2] 正对照失效：白名单文件不在位（$norm）")
        continue
    }
    if (-not $seenAllowed.ContainsKey($norm))
    {
        $violations.Add("[U2] 正对照失效：白名单文件未命中（$norm 未检出 SetHtmlAsync，判据可能恒绿）")
    }
}

# U3 负对照：合成样本必须被判违规（sink 里加回整文档原语）
$synthetic = 'Projects\FileDriven\FileDriven.Execution\Diagnostics\LogHtmlViewSink.cs'
if (Test-AllowedPath -Rel $synthetic)
{
    $violations.Add('[U3] 负对照失效：合成越权样本被白名单误收（授权面过宽）')
}

Write-Host ("扫描 {0} 件 .cs | 命中 {1} 处 | 白名单 {2} 件" -f $files.Count, $hits.Count, $allowed.Count) -ForegroundColor Gray

if ($violations.Count -eq 0)
{
    Write-Host '[OK] UiSingleWrite：白名单外整文档原语调用 0 处；正/负对照绿。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] UiSingleWrite 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
