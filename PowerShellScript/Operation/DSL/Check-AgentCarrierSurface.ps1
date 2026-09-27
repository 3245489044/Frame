# Check-AgentCarrierSurface.ps1
# Agent 架构冗余度量采集器（对齐 Projects\Plug\Agent\doc\Agent架构冗余彻底消除方案.md §1 R1–R5、§9 度量表）
#
# 用途：把方案 §1 的判定式 R1–R5 落成**可机器核对**的计数（方案 §9「采集方式 = 脚本扫描」的唯一落点；
#       Check-AgentRuntimeSlots.ps1 只管「静态可变槽 K10 + 键/取用面纪律 S4–S6」，不含承载面计数）。
#
# 采集项：
#   [R1] 承载面个数      —— 产物「登记→解析」通道计数（期望目标 = 1）
#        通道枚举：AppServiceProvider / DI-IServiceProvider / AgentRuntimeSlots+根分区 / AgentUiHost 槽位面
#   [R1'] 承载点位数     —— 各通道 Register/Resolve 调用点分文件计数（消灭承载面≠消灭点位；点位可迁往唯一面）
#   [R2] 装配入口公共层数 —— 装配入口 public API 计数（期望目标 = 1）
#   [R3] 配置读取 API 数  —— 配置读取 API 种类计数（期望目标 = 1）
#   [R5] 状态解析入口数   —— 状态/产物解析入口计数（期望目标 = 1）
#
# 模式：
#   - 报告模式（默认）：输出计数与分文件清单，不设退出码（迁移期看板）
#   - 校验模式（-Check）：R1 承载面 > 1 ∨ R2/R3/R5 超目标 → 退出码 1
#   - 白名单：迁移期载体（逐条登记理由，不得放宽为整目录泛白）
#
# 注：本脚本在 E1c 完成前**不登记进 GateManifests.psd1**（否则 Invoke-GateCheck -All 恒红）；
#     E1c 判据达成后转 -Check 并登记（见施工单 §6 E1c-6）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$scanDir = Join-Path $Root 'Projects\Plug\Agent\src'
if (-not (Test-Path $scanDir)) { Write-Error "未找到 Agent 源码目录: $scanDir"; exit 2 }

$files = Get-ChildItem -Path $scanDir -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

function Measure-Pattern {
    param([string]$Pattern)
    $hit = [System.Collections.Generic.List[object]]::new()
    foreach ($file in $files) {
        $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
        foreach ($m in (Select-String -Path $file.FullName -Pattern $Pattern -AllMatches -ErrorAction SilentlyContinue)) {
            $trimmed = $m.Line.Trim()
            # 注释行不计（与 Check-AgentRuntimeSlots.ps1 同口径）：判据只认代码事实，注释/文档引用不构成承载面
            if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('/*')) { continue }
            $hit.Add([pscustomobject]@{ File = $rel; Line = $m.LineNumber; Text = $trimmed })
        }
    }
    return $hit
}

# ── [R1] 承载面通道定义（值 = 登记/解析模式） ────────────────────────────────
$channels = [ordered]@{
    'AppServiceProvider'          = 'AppServiceProvider\.(Register|Resolve|TryResolve|IsRegistered)'
    'DI-IServiceProvider'         = '(services\.AddSingleton|GetRequiredService<|GetService<)'
    'AgentRuntimeSlots+根分区'     = 'AgentRuntimeSlots\.(Register|Resolve|TryResolve)|AgentRuntimeRoot\.(Resolve|TryResolve|RegisterStateCenter|ResolveStateCenter)'
    'AgentUiHost 槽位面'           = 'AgentUiHost\.(Attach[A-Za-z]*|Detach[A-Za-z]*)'
}

Write-Host '=== [R1] 产物承载面计数（目标 = 1） ==='
Write-Host ("扫描文件数: {0}" -f $files.Count)
$carrierCount = 0
$allHits = [System.Collections.Generic.List[object]]::new()
foreach ($kv in $channels.GetEnumerator()) {
    $hits = Measure-Pattern -Pattern $kv.Value
    foreach ($h in $hits) { $allHits.Add([pscustomobject]@{ Channel = $kv.Key; File = $h.File; Line = $h.Line; Text = $h.Text }) }
    $isCarrier = $hits.Count -gt 0
    if ($isCarrier) { $carrierCount++ }
    Write-Host ("  [{0}] {1}：点位 {2}" -f ($(if ($isCarrier) { '面' } else { '空' }), $kv.Key, $hits.Count))
}

Write-Host '--- [R1''] 承载点位分文件清单（Top 12） ---'
$allHits | Group-Object File | Sort-Object Count -Descending | Select-Object -First 12 |
    ForEach-Object { Write-Host ("  {0,3}  {1}" -f $_.Count, $_.Name) }

# ── [R2] 装配入口公共 API 计数（目标 = 1） ───────────────────────────────────
$entryHits = Measure-Pattern -Pattern 'public static async Task<(AgentFramework|AgentRuntime|AgentFrameworkAssemblyContext)>'
Write-Host ''
Write-Host '=== [R2] 装配入口公共 API 计数（目标 = 1） ==='
Write-Host ("  命中 {0}" -f $entryHits.Count)
$entryHits | ForEach-Object { Write-Host ("    {0}:{1} {2}" -f $_.File, $_.Line, $_.Text) }

# ── [R3] 配置读取 API 种类（目标 = 1） ──────────────────────────────────────
$cfgKinds = [ordered]@{
    'Section<T>()'        = '\.Section<[A-Za-z]+>\('
    'GetSection().Get<T>' = 'GetSection\(''[^'']+''\)\.Get<'
    'IConfiguration.Get<T>' = '\.Get<[A-Za-z]+(Options|Config)>\('
    'ConfigurationBuilder.AddJsonFile' = 'new ConfigurationBuilder\(\)|AddJsonFile\('
}
Write-Host ''
Write-Host '=== [R3] 配置读取 API 种类（目标 = 1） ==='
$cfgKindsUsed = 0
foreach ($kv in $cfgKinds.GetEnumerator()) {
    $hits = Measure-Pattern -Pattern $kv.Value
    if ($hits.Count -gt 0) { $cfgKindsUsed++ }
    Write-Host ("  {0,-34} 命中 {1}" -f $kv.Key, $hits.Count)
}

# ── [R5] 状态/产物解析入口（目标 = 1） ─────────────────────────────────────
$stateHits = Measure-Pattern -Pattern 'ResolveStateCenter\(|AgentRuntimeSlots\.(Resolve|TryResolve)|AppServiceProvider\.Resolve'
Write-Host ''
Write-Host '=== [R5] 状态/产物解析入口（目标 = 1） ==='
Write-Host ("  命中 {0}" -f $stateHits.Count)

# ── 汇总 ───────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '=== 汇总 ==='
Write-Host ("  R1 承载面个数          = {0}（目标 1）" -f $carrierCount)
Write-Host ("  R1'' 承载点位总数       = {0}" -f $allHits.Count)
Write-Host ("  R2 装配入口公共 API    = {0}（目标 1）" -f $entryHits.Count)
Write-Host ("  R3 配置读取 API 种类   = {0}（目标 1）" -f $cfgKindsUsed)
Write-Host ("  R5 状态解析入口点位    = {0}（目标 1）" -f $stateHits.Count)

if ($Check -and ($carrierCount -gt 1 -or $entryHits.Count -gt 1 -or $cfgKindsUsed -gt 1 -or $stateHits.Count -gt 1)) {
    Write-Host '校验失败：承载面/装配入口/配置读法/状态解析入口 未收敛到唯一面。'
    exit 1
}
exit 0
