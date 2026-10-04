#requires -Version 5.1
<#
.SYNOPSIS
    框架回退登记守卫（FallbackInventory）—— 「回退只能落到 T/N/D 三出口」由纪律变为机器判据。
    依据：Docs\analysis\2026\10\10.5\2026-10-04-框架回退逻辑彻底消除-重构方案.md §3/§5
.DESCRIPTION
    判据（命中即候选；增量超基线或未登记 ⇒ 违规）：
      F1  ?? new <Type>()：合法面 = 组合根文件（whitelistPaths）+ Null* 命名工厂 + nullFactories；其余计入棘轮。
      F2  catch 吞并：块体须含 throw / handle( / FailureBudget / Degrade，否则候选。
      F3  legacy 形参：legacy / compatibility / 旧面 / 旧路径 计数（棘轮只减不增）。
      F4  静默降级：行含「回退为/降级为/退化为」且同句无 AgentLog/ModuleLog/Log( ⇒ 候选。
      F5  [Obsolete] 带 dep：obsoleteTotal 与 obsoleteNoDep（缺 replaced-by/dep）双计数。
      F6  灰发开关：bool ... Use/Include/Prefer/Allow/Legacy/Compat = false 与 private const bool 计数。
    模式：默认报告模式；-Check 校验模式（增量/违规 → 退出码 1）。
    棘轮基线：同目录 fallback-baseline.json（只减不增；-UpdateBaseline 冻结；禁降基线过关）。
    登记表：同目录 fallback-registry.json（组合根白名单 + Null 工厂 + 合法项 T/N/D）。
.PARAMETER Root
    仓库超根（缺省 = 本脚本上溯四级；含 Frame\ 与 Projects\ 两个子模块）。
.PARAMETER Check
    校验模式：违规 → 退出码 1；缺省报告模式（不设退出码）。
.PARAMETER ShowDetail
    输出逐条命中明细。
.PARAMETER BaselinePath
    棘轮基线路径；缺省 = 同目录 fallback-baseline.json。
.PARAMETER RegistryPath
    登记表路径；缺省 = 同目录 fallback-registry.json。
.PARAMETER UpdateBaseline
    以本次实测重写基线（写操作，聚合器禁用）。
.EXAMPLE
    .\Check-FallbackInventory.ps1
    .\Check-FallbackInventory.ps1 -Check -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail,
    [string]$BaselinePath = '',
    [string]$RegistryPath = '',
    [switch]$UpdateBaseline
)

$ErrorActionPreference = 'Stop'

# ---- 路径定位（无硬编码绝对路径；缺省上溯四级 = 仓库超根，含 Frame\ 与 Projects\）----
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
$superRoot = $Root
if ([string]::IsNullOrWhiteSpace($BaselinePath)) { $BaselinePath = Join-Path $PSScriptRoot 'fallback-baseline.json' }
if ([string]::IsNullOrWhiteSpace($RegistryPath)) { $RegistryPath = Join-Path $PSScriptRoot 'fallback-registry.json' }

# 扫描面 = Frame 子模块 CsharpPrj ∪ Projects 子模块 Agent\src（跨子模块；缺失不静默）
$scanRoots = @()
$frameRoot = Join-Path $Root 'Frame\CsharpPrj'
if (Test-Path -LiteralPath $frameRoot) { $scanRoots += (Resolve-Path $frameRoot).Path }
$agentRoot = Join-Path $Root 'Projects\Plug\Agent\src'
if (Test-Path -LiteralPath $agentRoot) { $scanRoots += (Resolve-Path $agentRoot).Path }

$excludeRx = '\\(obj|bin|_deprecated|BuildOutput|backups)\\'

Write-Host '[FallbackInventory] 扫描面:' -ForegroundColor Cyan
foreach ($r in $scanRoots) { Write-Host ("  - {0}" -f $r) -ForegroundColor Cyan }
if ($scanRoots.Count -lt 2) { Write-Host '  [提示] agent 面缺失（仅扫 Frame 面）；缺失可见化，不静默' -ForegroundColor Yellow }

# ---- 登记表（组合根白名单 + Null 工厂）----
$whitelist = @()
$nullFactories = @()
$benignTypes = @()
$entries = @()
if (Test-Path -LiteralPath $RegistryPath) {
    try {
        $reg = Get-Content -LiteralPath $RegistryPath -Raw | ConvertFrom-Json
        if ($null -ne $reg.whitelistPaths) { $whitelist = @($reg.whitelistPaths) }
        if ($null -ne $reg.nullFactories) { $nullFactories = @($reg.nullFactories) }
        if ($null -ne $reg.benignTypePatterns) { $benignTypes = @($reg.benignTypePatterns) }
        if ($null -ne $reg.entries) { $entries = @($reg.entries) }
    } catch { Write-Host ("[登记表] 不可解析: {0}" -f $RegistryPath) -ForegroundColor Yellow }
} else {
    Write-Host ("[登记表] 缺失: {0}（白名单为空，全部 ?? new 计入棘轮）" -f $RegistryPath) -ForegroundColor Yellow
}

function Get-RelPath([string]$Full) {
    if ($Full.StartsWith($superRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Full.Substring($superRoot.Length).TrimStart('\')
    }
    return $Full
}
function Test-WhitelistFile([string]$Rel) {
    foreach ($w in $whitelist) { if ($Rel -like "*$w") { return $true } }
    return $false
}
# 合法回退登记项匹配：entry.file 为 $Rel 后缀（可带 :line）且 entry.target == 构造类型名。
function Test-RegistryEntry([string]$Rel, [string]$Type) {
    $relNorm = $Rel -replace '\\', '/'
    foreach ($e in $entries) {
        if ($null -eq $e -or [string]::IsNullOrWhiteSpace($e.target) -or [string]::IsNullOrWhiteSpace($e.file)) { continue }
        $f = (($e.file -split ':')[0]) -replace '\\', '/'
        if ($e.target -eq $Type -and $relNorm -like "*$f") { return $true }
    }
    return $false
}

# ---- 判据正则 ----
$rxF1          = [regex]'\?\?\s*new\s+(?<t>[A-Z][A-Za-z0-9_]*)'
$rxLegacy      = [regex]'(?i)\blegacy\b|\bcompatibility\b|旧面|旧路径'
$rxDegrade     = [regex]'回退为|降级为|退化为'
$rxVisible     = [regex]'AgentLog|ModuleLog|Log\('
$rxObsolete    = [regex]'\[Obsolete'
$rxObsoleteDep = [regex]'(?i)replaced-by|\bdep\b'
$rxGray        = [regex]'(?i)\bbool\s+\w*(use|include|prefer|allow|legacy|compat)\w*\s*=\s*false'
$rxConst       = [regex]'(?i)\bprivate\s+const\s+bool\b'
$rxComment     = [regex]'^\s*(//|/\*|\*)'

# ---- 正/负对照（防判据空转）----
$selfTestFail = 0
if (-not $rxF1.IsMatch('var x = y ?? new Foo();')) { $selfTestFail++ }
if ($rxF1.IsMatch('var x = y ?? null;')) { $selfTestFail++ }
if ($rxF1.Match('a ?? new NullThing()').Groups['t'].Value -ne 'NullThing') { $selfTestFail++ }
if ($selfTestFail -gt 0) { Write-Host '[自检] 正/负对照失败（判据可能被改坏）' -ForegroundColor Red }

# ---- F2：catch 吞并（块体无 throw/handle/FailureBudget/Degrade）----
function Measure-CatchSwallow([string[]]$Lines) {
    $count = 0; $i = 0
    while ($i -lt $Lines.Count) {
        $line = $Lines[$i]
        if ($rxComment.IsMatch($line) -or ($line -notmatch '\bcatch\b')) { $i++; continue }
        $depth = 0; $started = $false
        $sb = New-Object System.Text.StringBuilder
        $j = $i
        while ($j -lt $Lines.Count) {
            $code = ($Lines[$j] -replace '//.*$', '')
            [void]$sb.Append($code).Append(' ')
            foreach ($ch in $code.ToCharArray()) {
                if ($ch -eq '{') { $depth++; $started = $true } elseif ($ch -eq '}') { $depth-- }
            }
            if ($started -and $depth -le 0) { break }
            $j++
            if (($j - $i) -gt 300) { break }
        }
        $body = $sb.ToString()
        if ($body -notmatch '\bthrow\b' -and $body -notmatch 'handle\s*\(' -and $body -notmatch 'FailureBudget' -and $body -notmatch '\bDegrade\b') { $count++ }
        $i = $j + 1
    }
    return $count
}

# ---- 扫描 ----
$falseNew = 0; $collectionNormalize = 0; $registeredLegit = 0; $catchSwallow = 0; $legacyParam = 0; $silentDegrade = 0
$obsoleteTotal = 0; $obsoleteNoDep = 0; $grayFlags = 0

foreach ($scanRoot in $scanRoots) {
    $files = @(Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch $excludeRx })
    foreach ($f in $files) {
        $rel = Get-RelPath $f.FullName
        $lines = [System.IO.File]::ReadAllLines($f.FullName)
        $isWl = Test-WhitelistFile $rel
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $ln = $lines[$i]
            # F1（合法面：组合根白名单 / Null* 工厂 / nullFactories / 集合归一 benignTypePatterns）
            $m1 = $rxF1.Match($ln)
            if ($m1.Success) {
                $t = $m1.Groups['t'].Value
                if ($benignTypes -contains $t -or $t.Length -le 1) {
                    $collectionNormalize++
                } elseif ($isWl -or $t.StartsWith('Null') -or ($nullFactories -contains $t)) {
                    # 组合根 / Null* 工厂 / 显式空对象：合法面，不计
                } elseif (Test-RegistryEntry -Rel $rel -Type $t) {
                    $registeredLegit++
                } else {
                    $falseNew++
                    if ($ShowDetail) { Write-Host ("  [F1] {0}:{1}  {2}" -f $rel, ($i + 1), $ln.Trim()) }
                }
            }
            if ($rxComment.IsMatch($ln)) { continue }
            # F3
            if ($rxLegacy.IsMatch($ln)) { $legacyParam++ }
            # F4
            if ($rxDegrade.IsMatch($ln) -and -not $rxVisible.IsMatch($ln)) { $silentDegrade++ }
            # F5
            if ($rxObsolete.IsMatch($ln)) {
                $obsoleteTotal++
                if (-not $rxObsoleteDep.IsMatch($ln)) { $obsoleteNoDep++ }
            }
            # F6
            if ($rxGray.IsMatch($ln) -or $rxConst.IsMatch($ln)) { $grayFlags++ }
        }
        $catchSwallow += Measure-CatchSwallow $lines
    }
}

# ---- 汇总 ----
$measured = [ordered]@{
    falseNew           = $falseNew
    collectionNormalize = $collectionNormalize
    registeredLegit    = $registeredLegit
    catchSwallow       = $catchSwallow
    legacyParam   = $legacyParam
    silentDegrade = $silentDegrade
    obsoleteTotal = $obsoleteTotal
    obsoleteNoDep = $obsoleteNoDep
    grayFlags     = $grayFlags
}
Write-Host ''
Write-Host (('[FallbackInventory] ?? new真源={0} / 集合归一={1} / 已登记={2} / catch吞并={3} / legacy={4} / 静默降级={5} / Obsolete={6}(缺dep={7}) / 灰发开关={8}' ) -f `
    $measured.falseNew, $measured.collectionNormalize, $measured.registeredLegit, $measured.catchSwallow, $measured.legacyParam, $measured.silentDegrade, $measured.obsoleteTotal, $measured.obsoleteNoDep, $measured.grayFlags) -ForegroundColor Cyan

# ---- 棘轮（只减不增；照抄 Check-LogMessages 检查6 口径）----
if ($UpdateBaseline) {
    $payload = [ordered]@{
        version   = '1.0'
        generated = (Get-Date -Format 'yyyy-MM-dd')
        scan      = [ordered]@{ frame = 'Frame\CsharpPrj'; agent = 'Projects\Plug\Agent\src'; exclude = @('obj', 'bin', '_deprecated', 'BuildOutput', 'backups') }
        counts    = $measured
        note      = 'C1–C6 回退计数只减不增基线（对齐 Check-LogMessages 棘轮）。首跑由 -UpdateBaseline 冻结；增量即红；禁降基线过关。'
    }
    $payload | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $BaselinePath -Encoding UTF8
    Write-Host ("[棘轮] 基线已写入: {0}" -f $BaselinePath) -ForegroundColor Cyan
    exit 0
}

$ratchetErrors = 0
if (Test-Path -LiteralPath $BaselinePath) {
    $base = $null
    try { $base = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json } catch { $base = $null }
    if ($null -eq $base -or $null -eq $base.counts) {
        Write-Host ("[棘轮] 基线不可解析: {0}（仅报告不拦；重建用 -UpdateBaseline）" -f $BaselinePath) -ForegroundColor Yellow
    } else {
        foreach ($k in $measured.Keys) {
            $bv = 0; if ($null -ne $base.counts.$k) { $bv = [int]$base.counts.$k }
            $cv = [int]$measured[$k]
            if ($cv -gt $bv) {
                $ratchetErrors++
                Write-Host ("[棘轮] {0} 增量违规: 实测 {1} > 基线 {2}（新增须登记/清理，禁降基线通过）" -f $k, $cv, $bv) -ForegroundColor Red
            } elseif ($cv -lt $bv) {
                Write-Host ("[棘轮] {0} 低于基线: 实测 {1} < 基线 {2}（可 -UpdateBaseline 收紧）" -f $k, $cv, $bv) -ForegroundColor Yellow
            }
        }
        if ($ratchetErrors -eq 0) { Write-Host '[棘轮] 基线棘轮通过（全部计数未增量）' -ForegroundColor Green }
    }
} else {
    Write-Host ("[棘轮] 基线缺失: {0}（仅报告不拦；建基线用 -UpdateBaseline）" -f $BaselinePath) -ForegroundColor Yellow
}

if ($selfTestFail -gt 0) { Write-Host ("[自检] 正/负对照失败 {0} 项" -f $selfTestFail) -ForegroundColor Red }

# ---- 退出码：-Check 时（棘轮增量 ∨ 自检失败）→ 1 ----
$exitCode = 0
if ($Check -and ($ratchetErrors -gt 0 -or $selfTestFail -gt 0)) { $exitCode = 1 }
exit $exitCode
