#requires -Version 5.1
<#
.SYNOPSIS
    WebView2 线程亲和守卫（UiThreadAffinity）——「渲染入口在 UI 线程」必须是代码结构判据，不能是注释自述。

.DESCRIPTION
    根因（Docs\analysis\2026\10\10.25\2026-10-25-HtmlViewer首帧渲染跨线程失败-根因与修复施工单.md §二/§三）：
    封送器只保证「action 开始时在 UI 线程」；WebViewLifecycleHost 内部 await 续体若回落线程池，
    随后的 EnsureCoreWebView2Async / CoreWebView2 / NavigateToString 全部跨线程 ⇒ 首帧初始化失败；
    失败经 sink 的「先记日志、后退避」顺序放大，再由日志自反驱动一次递归重挂。

    判据（结构事实：源码文本 / 行号序；禁执行者叙述）：
      U1 LifecycleHost 内 `.ConfigureAwait(false)` 调用 = 0（注释行不计）。
      U2 IWebViewCore 实现体含线程守卫（InvokeRequired）≥ 基线，且异常文本带成员名（`IWebViewCore.{member}`）。
      U3 sink 的两个失败 catch：诊断必经 LogSelfDiagnostic（禁裸 ExecutionLog.Log 直呼），且退避置位在诊断行之前。
      U4 正对照：UiThreadMarshaler 定义恰 1（封送判据只有一份实现）。
      U5 负对照（判据自证）：同一匹配器对「含 ConfigureAwait(false)」「干净」「注释」三种样本各自判定正确。
      U6 首帧不空窗：`_fullFailStreak` 引用 ≥ 基线 ∧ `NextFullRetryBackoffMs(` 在位 ∧ 固定 `+ 1000` 退避字面量 = 0。
      U7 诊断可见化：消息册中 render_failed 模板含 {threadId}（失败须带线程事实，禁静默）。
      L1/L2 活性自检：扫描面文件须真实含断言/自反入口（防「扫描面失效 ⇒ 恒绿」）。

    模式：默认报告（不设退出码）；-Check 时违规 → 退出码 1；前置缺失（基线引用的文件不在位）→ 退出码 2。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\Meta\，上溯五级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$b = Import-GateBaseline -Name UiThreadAffinity

$lifecycleRel  = [string]$b.LifecycleHostFile.Value
$coreRel       = [string]$b.CoreHostFile.Value
$sinkRel       = [string]$b.SinkFile.Value
$catalogRel    = [string]$b.MessageCatalogFile.Value
$marshalerDir  = [string]$b.MarshalerDir.Value
$minGuards     = [int]$b.MinCoreGuards.Value
$marshalerDefs = [int]$b.MarshalerDefinitionCount.Value
$minStreakRefs = [int]$b.MinStreakRefs.Value

$violations = [System.Collections.Generic.List[string]]::new()

function Resolve-RelPath {
    param([Parameter(Mandatory = $true)][string]$Rel)
    return (Join-Path $Root ($Rel -replace '/', '\'))
}

$lifecycleAbs = Resolve-RelPath -Rel $lifecycleRel
$coreAbs      = Resolve-RelPath -Rel $coreRel
$sinkAbs      = Resolve-RelPath -Rel $sinkRel
$catalogAbs   = Resolve-RelPath -Rel $catalogRel
$marshalerAbs = Resolve-RelPath -Rel $marshalerDir

Write-Host '=== [UiThreadAffinity] WebView2 线程亲和（入口断言 / 禁续体逃逸 / 失败不自反） ==='

# ---------------- 前置（判据无标的 ⇒ 退出码 2，禁静默通过） ----------------
$missing = @()
foreach ($p in @($lifecycleAbs, $coreAbs, $sinkAbs, $catalogAbs, $marshalerAbs))
{
    if (-not (Test-Path -LiteralPath $p)) { $missing += $p }
}
if ($missing.Count -gt 0)
{
    Write-Host '[UiThreadAffinity] 前置缺失（判据不可判）：' -ForegroundColor Red
    foreach ($m in $missing) { Write-Host ("  - {0}" -f $m) -ForegroundColor Red }
    exit 2
}

# ---------------- 匹配器（唯一口径；U1/U5 共用，防两套正则漂移） ----------------
# 调用形（非注释）：`.ConfigureAwait(false)`；注释行由 Test-CodeLineHit 统一剔除。
$configureAwaitPattern = '\.ConfigureAwait\(false\)'

function Test-CodeLineHit {
    param(
        [string]$Line,
        [Parameter(Mandatory = $true)][string]$Pattern
    )
    if ([string]::IsNullOrWhiteSpace($Line)) { return $false }
    if ($Line.Trim().StartsWith('//')) { return $false }
    return [regex]::IsMatch($Line, $Pattern)
}

function Get-CodeFileHits {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern
    )
    $out = [System.Collections.Generic.List[int]]::new()
    $lines = [System.IO.File]::ReadAllLines($Path)
    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        if (Test-CodeLineHit -Line $lines[$i] -Pattern $Pattern) { $out.Add($i + 1) }
    }
    return $out
}

function Find-FirstLineInFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern
    )
    $lines = [System.IO.File]::ReadAllLines($Path)
    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        if (Test-CodeLineHit -Line $lines[$i] -Pattern $Pattern) { return $i + 1 }
    }
    return -1
}

function Get-CodeFileLineCount {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ([System.IO.File]::ReadAllLines($Path)).Count
}

# ---------------- U5 负对照（判据自证；恒跑，防匹配器失效导致恒绿） ----------------
$dirtyHits   = if (Test-CodeLineHit -Line 'await _host.EnsureCoreAsync().ConfigureAwait(false);' -Pattern $configureAwaitPattern) { 1 } else { 0 }
$cleanHits   = if (Test-CodeLineHit -Line 'await _host.EnsureCoreAsync();' -Pattern $configureAwaitPattern) { 1 } else { 0 }
$commentHits = if (Test-CodeLineHit -Line '// 禁用 .ConfigureAwait(false)：续体回线程池会让 WebView2 跨线程访问' -Pattern $configureAwaitPattern) { 1 } else { 0 }
if ($dirtyHits -ne 1) { $violations.Add('[U5] 负对照失效：含 ConfigureAwait(false) 的样本未被判违规（匹配器失效 ⇒ 判据恒绿）') }
if ($cleanHits -ne 0) { $violations.Add('[U5] 正对照失效：干净样本被误判（假红）') }
if ($commentHits -ne 0) { $violations.Add('[U5] 注释剔除失效：注释行被计入（会逼出删注释式假修复）') }

# ---------------- U1 续体逃逸 = 0 ----------------
$escapeHits = @(Get-CodeFileHits -Path $lifecycleAbs -Pattern $configureAwaitPattern)
foreach ($ln in $escapeHits)
{
    $violations.Add(("[U1] {0}:{1} 使用 ConfigureAwait(false) ⇒ await 续体可回落线程池，随后 CoreWebView2 访问逃逸 UI 线程" -f $lifecycleRel, $ln))
}

# ---------------- U2 IWebViewCore 端口守卫 ----------------
$guardLines = @(Get-CodeFileHits -Path $coreAbs -Pattern 'AssertUiThread\(nameof\(IWebViewCore\.')
if ($guardLines.Count -lt $minGuards)
{
    $violations.Add(("[U2] {0} 线程守卫命中 {1} < 基线 {2}（IWebViewCore 成员须跨线程即抛）" -f $coreRel, $guardLines.Count, $minGuards))
}
if (-not ([System.IO.File]::ReadAllText($coreAbs)).Contains('IWebViewCore.{member}'))
{
    $violations.Add(("[U2] {0} 守卫异常文本缺成员名（须含 'IWebViewCore.{{member}}' ⇒ 跨线程调用方可定位）" -f $coreRel))
}

# ---------------- U3 sink：顺序契约 + 诊断唯一入口 ----------------
$backoffLine = Find-FirstLineInFile -Path $sinkAbs -Pattern '_fullRetryNotBeforeMs\s*=\s*Environment\.TickCount64'
$fullLogLine = Find-FirstLineInFile -Path $sinkAbs -Pattern 'LogSelfDiagnostic\(\s*"LogHtmlViewSink_render_failed"'
if ($backoffLine -lt 0) { $violations.Add(("[U3] {0} 未找到整页失败退避置位（_fullRetryNotBeforeMs =）" -f $sinkRel)) }
if ($fullLogLine -lt 0) { $violations.Add(("[U3] {0} 整页失败诊断未走 LogSelfDiagnostic（缺自反抑制入口）" -f $sinkRel)) }
if ($backoffLine -gt 0 -and $fullLogLine -gt 0 -and $backoffLine -gt $fullLogLine)
{
    $violations.Add(("[U3] {0} 顺序反了：退避行 {1} 在诊断行 {2} 之后（诊断日志会以旧状态驱动自激重挂）" -f $sinkRel, $backoffLine, $fullLogLine))
}

$bareDiagnostics = @(Get-CodeFileHits -Path $sinkAbs -Pattern 'ExecutionLog\.Log\(\s*"LogHtmlViewSink_(render_failed|append_failed)"')
foreach ($ln in $bareDiagnostics)
{
    $violations.Add(("[U3] {0}:{1} 失败诊断直呼 ExecutionLog.Log ⇒ 日志自反递归重试（须经 LogSelfDiagnostic）" -f $sinkRel, $ln))
}
foreach ($msgId in @('LogHtmlViewSink_render_failed', 'LogHtmlViewSink_append_failed'))
{
    if ((Find-FirstLineInFile -Path $sinkAbs -Pattern ('LogSelfDiagnostic\(\s*"' + $msgId + '"')) -lt 0)
    {
        $violations.Add(("[U3] {0} 缺少 LogSelfDiagnostic 入口：{1}" -f $sinkRel, $msgId))
    }
}

# ---------------- U4 正对照：封送判据唯一实现 ----------------
$marshalerFiles = @(Get-ChildItem -LiteralPath $marshalerAbs -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue)
$marshalerHits = 0
foreach ($f in $marshalerFiles)
{
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName))
    {
        if ($line.Trim().StartsWith('//')) { continue }
        if ($line -match 'class\s+UiThreadMarshaler\b') { $marshalerHits++ }
    }
}
if ($marshalerHits -ne $marshalerDefs)
{
    $violations.Add(("[U4] UiThreadMarshaler 定义 {0} 处 ≠ 基线 {1}（封送判据须唯一实现，禁第二套线程判定）" -f $marshalerHits, $marshalerDefs))
}

# ---------------- U6 首帧不空窗（退避按失败次数，非固定 1s） ----------------
$streakRefs = @(Get-CodeFileHits -Path $sinkAbs -Pattern '_fullFailStreak').Count
if ($streakRefs -lt $minStreakRefs)
{
    $violations.Add(("[U6] {0} 连续失败计数引用 {1} < 基线 {2}（退避须按失败次数递增，首次失败不空窗）" -f $sinkRel, $streakRefs, $minStreakRefs))
}
if ((Find-FirstLineInFile -Path $sinkAbs -Pattern 'NextFullRetryBackoffMs\(') -lt 0)
{
    $violations.Add(("[U6] {0} 缺 NextFullRetryBackoffMs( 退避计算入口" -f $sinkRel))
}
$fixedBackoff = @(Get-CodeFileHits -Path $sinkAbs -Pattern '\+\s*1000\b').Count
if ($fixedBackoff -gt 0)
{
    $violations.Add(("[U6] {0} 仍含固定 '+ 1000' 退避字面量 {1} 处（首帧空窗不可收敛）" -f $sinkRel, $fixedBackoff))
}

# ---------------- U7 诊断可见化：失败消息必须带线程事实 ----------------
$catalogText = [System.IO.File]::ReadAllText($catalogAbs)
$renderBlock = [regex]::Match($catalogText, '"LogHtmlViewSink_render_failed"\s*:\s*\{(?s).*?"template"\s*:\s*"(?<tpl>[^"]*)"')
if (-not $renderBlock.Success)
{
    $violations.Add(("[U7] {0} 缺 LogHtmlViewSink_render_failed 定义" -f $catalogRel))
}
elseif ($renderBlock.Groups['tpl'].Value -notmatch '\{threadId\}')
{
    $violations.Add(("[U7] {0} 的 render_failed 模板缺 {{threadId}}（跨线程失败须可归因）" -f $catalogRel))
}

# ---------------- L1/L2 活性自检（防「扫描面失效 ⇒ 恒绿」） ----------------
if (@(Get-CodeFileHits -Path $lifecycleAbs -Pattern 'AssertUiThread').Count -eq 0)
{
    $violations.Add(("[L1] {0} 未见 AssertUiThread（扫描面失效或入口断言被移除）" -f $lifecycleRel))
}
if (@(Get-CodeFileHits -Path $sinkAbs -Pattern 'LogSelfDiagnostic\(').Count -eq 0)
{
    $violations.Add(("[L2] {0} 未见 LogSelfDiagnostic(（诊断唯一入口缺失）" -f $sinkRel))
}

$lifecycleLineCount = Get-CodeFileLineCount -Path $lifecycleAbs
$coreLineCount      = Get-CodeFileLineCount -Path $coreAbs
$sinkLineCount      = Get-CodeFileLineCount -Path $sinkAbs
Write-Host ("扫描：lifecycle={0} 行 | coreHost={1} 行 | sink={2} 行 | marshaler={3} 文件 | 封送器定义={4}" -f $lifecycleLineCount, $coreLineCount, $sinkLineCount, $marshalerFiles.Count, $marshalerHits) -ForegroundColor Gray
Write-Host ("实测：续体逃逸={0} | 端口守卫={1}（基线 {2}）| 退避行={3} 诊断行={4} | 连续失败引用={5}（基线 {6}）| 负对照={7}/{8}/{9}" -f $escapeHits.Count, $guardLines.Count, $minGuards, $backoffLine, $fullLogLine, $streakRefs, $minStreakRefs, $dirtyHits, $cleanHits, $commentHits) -ForegroundColor Gray
if ($ShowDetail)
{
    $u1 = if ($escapeHits.Count -gt 0) { $escapeHits -join ',' } else { '无' }
    Write-Host ("  [U1] lifecycle 命中行号：{0}" -f $u1) -ForegroundColor DarkGray
    Write-Host ("  [U2] InvokeRequired 行号：{0}" -f ($guardLines -join ',')) -ForegroundColor DarkGray
    Write-Host ("  [U6] _fullFailStreak 引用数：{0}" -f $streakRefs) -ForegroundColor DarkGray
}

if ($violations.Count -eq 0)
{
    Write-Host '[OK] UiThreadAffinity：续体逃逸 0 处；端口守卫/顺序契约/退避/可见化在册；正负对照绿。' -ForegroundColor Green
    exit 0
}

Write-Host ("[FAIL] UiThreadAffinity 命中 {0} 项：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - {0}" -f $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
