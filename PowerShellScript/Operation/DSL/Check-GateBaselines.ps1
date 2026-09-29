#requires -Version 5.1
<#
.SYNOPSIS
    内联基线守卫（GateBaselines U1–U6）—— 「脚本内禁内联数值基线」由注释纪律变为机器判据。

.DESCRIPTION
    背景：NodeGateCommon.ps1:8 早已声明「基线一律外置到 Baselines\<Gate>.psd1，禁散落脚本常量」，
    但无判据 ⇒ 13 个 Check-*.ps1 仍内联数值基线（实测 2026-09-29）。本件把该纪律变成 fail-closed 判据。

    判据：
      U1 清点：扫描面内 Check-*.ps1 的内联数值基线落点（口径 = GateBaseline.psm1 Get-InlineBaselineRegex，唯一正则）。
      U2 在册（拦新增）：U1 命中文件必须在 Baselines\_inline-baselines.psd1 在册；未在册 ⇒ 违规。
      U3 类型：单件基线文件的事实行必带 Origin ∈ {Derived,Debt,Verdict}；Debt/Verdict 必带 DueWhen。
      U4 正对照（防判据空转）：单件基线文件数 ≥ 1，且 KeyGrammar.psd1 的 K1 事实 ≥ 7 条。
      U5 负对照（判据自证）：合成样本必须被同一正则捕获；非赋值式样必须不命中。
      U6 可见化：Migrated=$false 的在册项列 [待迁移]；在册但零命中列 [可销账]（不拦）。
      U7 接线一致性：psd1 须被同名消费脚本装载，且每个事实键名须在消费脚本中出现（防拼写漂移 ⇒ 静默降级为 0）。
      U8 命名唯一性：规范函数名定义数 > 1 即红；退役名（第二基线根）再入即红。
      U9 事实行类型强制：单件基线的每个事实键须为含 Origin 的事实行（裸值/无 Origin 行即红）。
      U10 声明面路径存在性：声明面引用的 `…\Baselines\<X>.psd1` 必须存在（防声明面腐化）。
      U11 行形态唯一：事实行须为 `@{ Value; Origin; DueWhen }`（禁 `Baseline`/`RelativePath` 等第二形态）。

    模式：默认报告模式；-Check 校验模式（U1..U5/U7..U11 违规 → 退出码 1；U6 永不拦）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐条实测（含待迁移/可销账清单）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  门禁基线守卫（内联基线清册 U1–U11）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

# 扫描面 = 「目录规约（Frame\PowerShellScript 全域）」 ∪ 「GateManifests 登记的 Path 所在目录」
#   动机（G2 实测）：此前扫描面硬编码为 Frame\PowerShellScript ⇒ 登记在 Modules/Projects 侧（或未来落在别处）的
#   Check-*.ps1 不在守卫面内 ⇒ 新增内联基线无人拦（与「判据载体率」同族的覆盖漏洞）。
$scanRoots = [System.Collections.Generic.List[string]]::new()
$scanRoots.Add((Join-Path $Root 'Frame\PowerShellScript'))
$manifestsPath = Join-Path $Root 'Frame\PowerShellScript\Operation\DSL\GateManifests.psd1'
if (Test-Path -LiteralPath $manifestsPath)
{
    $gateSpecs = Import-PowerShellDataFile -LiteralPath $manifestsPath
    foreach ($gateName in $gateSpecs.Keys)
    {
        $spec = $gateSpecs[$gateName]
        if ($spec -isnot [System.Collections.IDictionary] -or -not $spec.Contains('Path')) { continue }
        $specPath = [string]$spec.Path
        if ([string]::IsNullOrWhiteSpace($specPath)) { continue }
        $specDir = Split-Path -Parent (Join-Path $Root $specPath)
        if ((Test-Path -LiteralPath $specDir) -and -not $scanRoots.Contains($specDir)) { $scanRoots.Add($specDir) }
    }
}

$scripts = @()
foreach ($scanRoot in $scanRoots)
{
    $scripts += @(Get-ChildItem -LiteralPath $scanRoot -Recurse -Filter 'Check-*.ps1' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\_deprecated\\' })
}
$scripts = @($scripts | Sort-Object -Property FullName -Unique)

$rxInline = Get-InlineBaselineRegex

# ---------------- U1 内联数值基线清点 ----------------
$inlineHits = [System.Collections.Generic.List[object]]::new()
foreach ($script in $scripts)
{
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($script.FullName))
    {
        $lineNo++
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith('#')) { continue }
        foreach ($m in [regex]::Matches($trimmed, $rxInline))
        {
            $inlineHits.Add([pscustomobject]@{
                File = $script.Name; Path = $script.FullName; Line = $lineNo
                Name = $m.Groups['name'].Value; Value = $m.Groups['value'].Value
            })
        }
    }
}
$hitFiles = @($inlineHits | ForEach-Object { $_.File } | Sort-Object -Unique)

# ---------------- U2 在册性 ----------------
$registerPath = Join-Path $Root 'Frame\PowerShellScript\Baselines\_inline-baselines.psd1'
$registered = @{}
if (-not (Test-Path -LiteralPath $registerPath))
{
    $violations.Add("[U2] 内联基线清册缺失：Frame\PowerShellScript\Baselines\_inline-baselines.psd1")
}
else
{
    $register = Import-PowerShellDataFile -LiteralPath $registerPath
    foreach ($entry in @($register.Entries)) { $registered[[string]$entry.File] = $entry }
}

foreach ($file in $hitFiles)
{
    if (-not $registered.ContainsKey($file))
    {
        $sample = @($inlineHits | Where-Object { $_.File -eq $file } | Select-Object -First 1)
        $violations.Add("[U2] 新增内联数值基线未在册：${file}:$($sample.Line) `$$($sample.Name) = $($sample.Value)" +
            "（改法见下：外置 Baselines\<Gate>.psd1 或**粘贴**下列登记行到 Baselines\_inline-baselines.psd1）")
        $violations.Add("     @{ File = '$file'; Origin = 'Debt'; Migrated = `$false; DueWhen = '待分类（SR-1b）' }")
    }
}

# ---------------- U3 外置基线类型 ----------------
$baselineFiles = @(Get-BaselineFiles)
foreach ($bf in $baselineFiles)
{
    $data = Import-PowerShellDataFile -LiteralPath $bf.FullName
    foreach ($v in (Test-BaselineEntryTyped -Name $bf.Name -Data $data)) { $violations.Add($v) }
}

# ---------------- U4 正对照 ----------------
if ($baselineFiles.Count -lt 1)
{
    $violations.Add('[U4] 单件基线文件数 0（判据无标的 ⇒ 假绿）')
}
$keyGrammarBaseline = Join-Path $Root 'Frame\PowerShellScript\Baselines\KeyGrammar.psd1'
if (-not (Test-Path -LiteralPath $keyGrammarBaseline))
{
    $violations.Add('[U4] Baselines\KeyGrammar.psd1 缺失（K1–K6 基线单源被删 ⇒ 判据失效）')
}
else
{
    $kg = Import-PowerShellDataFile -LiteralPath $keyGrammarBaseline
    $k1Count = @($kg.KeyFamilyLiterals).Count
    if ($k1Count -lt 7)
    {
        $violations.Add("[U4] KeyGrammar.psd1 的 K1 事实 $k1Count 条 < 7（基线被掏空 ⇒ 判据失效）")
    }
}

# ---------------- U5 负对照（判据自证） ----------------
# 样本按**拼接**构造：字面量不得在本文件内成形，否则 U1 会命中本文件自身（自指假阳）。
$posSample = '$sampleBase' + 'line = 7'
$negSample = '$base' + 'lineCount++'
$positive = [regex]::Match($posSample, $rxInline)
if (-not $positive.Success)
{
    $violations.Add('[U5] 负对照失效：合成内联基线样本未被捕获（正则被改坏）')
}
$negative = [regex]::Match($negSample, $rxInline)
if ($negative.Success)
{
    $violations.Add('[U5] 负对照失效：非赋值式样被误捕（正则过宽）')
}

# ---------------- U6 可见化 ----------------
foreach ($file in ($registered.Keys | Sort-Object))
{
    $entry = $registered[$file]
    $isMigrated = $false
    if ($entry.ContainsKey('Migrated')) { $isMigrated = [bool]$entry.Migrated }
    if (-not $isMigrated)
    {
        $hitCount = @($inlineHits | Where-Object { $_.File -eq $file }).Count
        if ($hitCount -eq 0)
        {
            $pending.Add("[可销账] $file 已无内联基线命中（迁移完成后从清册删除）")
        }
        else
        {
            $pending.Add("[待迁移] $file 内联基线 $hitCount 处（Origin=$($entry.Origin)）| DueWhen: $($entry.DueWhen)")
        }
    }
}

# ---------------- U7 接线一致性（基线键 ↔ 消费脚本；防静默降级） ----------------
# 动机：消费端 `[int]$g.Key.Value` 若键名拼错 ⇒ [int]$null = 0 ⇒ 判据静默失效（「≥ 基线」永真）。
# 判据：① 消费脚本须装载本基线；② psd1 的每个事实键名须在消费脚本中出现（`.<Key>`）。
foreach ($bf in $baselineFiles)
{
    $gateName = [IO.Path]::GetFileNameWithoutExtension($bf.Name)
    $consumer = @($scripts | Where-Object { $_.Name -eq "Check-$gateName.ps1" })
    if ($consumer.Count -eq 0)
    {
        $pending.Add("[接线] $gateName.psd1 无同名消费脚本（Check-$gateName.ps1）—— 遗留子根或已改名，请核对")
        continue
    }

    $consumerText = Get-Content -LiteralPath $consumer[0].FullName -Raw -Encoding UTF8
    if ($consumerText -notmatch "Import-GateBaseline\s+-Name\s+$gateName\b")
    {
        $violations.Add("[U7] $gateName.psd1 未在 Check-$gateName.ps1 中装载（缺 Import-GateBaseline -Name $gateName）⇒ 基线未生效")
    }

    $data = Import-PowerShellDataFile -LiteralPath $bf.FullName
    foreach ($k in $data.Keys)
    {
        $keyName = [string]$k
        if ($keyName -in @('Schema', 'Gate', 'Source', 'Canonical', 'Notes')) { continue }
        if ($consumerText -notmatch "\.$keyName\b")
        {
            $violations.Add("[U7] $gateName.psd1 的键 '$keyName' 未出现在 Check-$gateName.ps1（拼写漂移/未接线 ⇒ 消费端取 `$null 静默降级为 0）")
        }
    }
}

# ---------------- U8 名字唯一性（规范函数名禁再定义 ⇒ 防绑定到旧实现） ----------------
# 动机（SR-1f 实测）：同名两实现时，消费脚本可能绑到**旧实现**（不同根）⇒ 找不到 psd1 而抛错，
#   且 ps-function-index 的「多属主」本身无人消费。判据 = 文本定义唯一性（<2 处即合规）。
$allPs = @()
foreach ($scanRoot in $scanRoots)
{
    $allPs += @(Get-ChildItem -LiteralPath $scanRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -in @('.ps1', '.psm1') -and $_.FullName -notmatch '\\_deprecated\\' })
}
$allPs = @($allPs | Sort-Object -Property FullName -Unique)
# 规范名：定义数 > 1 即红（消费端可能绑到非规范实现）
# 退役名：定义数 > 0 即红（SR-2a 并根后**禁第二基线根**——其唯一实现已于遗留子根退役）
$canonicalNames = @('Import-GateBaseline', 'Get-BaselinesRoot', 'Get-BaselineFiles', 'Test-BaselineEntryTyped', 'Get-InlineBaselineRegex')
$retiredNames = @('Import-NodeGateBaseline', 'Get-LegacyBaselinesRoots')
foreach ($fn in ($canonicalNames + $retiredNames))
{
    $defs = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $allPs)
    {
        $text = Get-Content -LiteralPath $f.FullName -Raw -Encoding UTF8
        if ($text -match "function\s+$fn\b") { $defs.Add($f.Name) }
    }
    if ($fn -in $canonicalNames -and $defs.Count -gt 1)
    {
        $violations.Add("[U8] 函数 $fn 有 $($defs.Count) 处定义（$($defs -join ' / ')）⇒ 消费端可能绑到非规范实现；规范实现须唯一")
    }
    if ($fn -in $retiredNames -and $defs.Count -gt 0)
    {
        $violations.Add("[U8] 退役名 $fn 再入（$($defs -join ' / ')）⇒ 禁第二基线根；一律用 Baselines\GateBaseline.psm1 的 Import-GateBaseline")
    }
}

# ---------------- U9 事实行类型强制（README「每条基线必带 Origin」的可判形式） ----------------
# 动机（G1 实测）：U3 只在「该行**已带** Origin」时才校验 ⇒ 裸值行（int / 字符串 / 字符串数组）恒绿，
#   外置基线可退化为「另一种散落常量」而无人拦（遗留 AgentFramework.psd1 全表裸值即标本）。
# 判据：单件基线的每个顶层事实键（除结构键）须为**事实行**哈希表（含 Origin）；行数组的每个元素亦然。
$structuralKeys = @('Schema', 'Gate', 'Source', 'Canonical', 'Notes', 'Measured')
foreach ($bf in $baselineFiles)
{
    $gateName = [IO.Path]::GetFileNameWithoutExtension($bf.Name)
    $data = Import-PowerShellDataFile -LiteralPath $bf.FullName
    foreach ($k in $data.Keys)
    {
        $keyName = [string]$k
        if ($keyName -in $structuralKeys) { continue }
        $value = $data[$k]
        $rows = @()
        if ($value -is [System.Collections.IDictionary]) { $rows = @($value) }
        elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) { $rows = @($value) }

        if ($rows.Count -eq 0)
        {
            $typeName = if ($null -eq $value) { '<null>' } else { $value.GetType().Name }
            $violations.Add("[U9] $gateName.psd1 的 '$keyName' 是裸值（$typeName）⇒ 须改事实行 @{ Value=…; Origin=Derived|Debt|Verdict; DueWhen=… }")
            continue
        }

        foreach ($row in $rows)
        {
            if ($row -isnot [System.Collections.IDictionary] -or -not $row.Contains('Origin'))
            {
                $violations.Add("[U9] $gateName.psd1 的 '$keyName' 含非事实行元素（缺 Origin）⇒ 类型不受控，U3 会静默跳过")
                break
            }
        }
    }
}

# ---------------- U10 声明面路径存在性（StructuralGrounding 的 PowerShell 实现；收窄 Baselines 面） ----------------
# 动机（G4 实测）：GateManifests 的 Note 曾写 `Baselines\AgentFramework.psd1` 而该路径不存在（真身 = 遗留子根）
#   ⇒ 声明面会腐且无人拦（R5 标本）。SR-2a3 扩面：门禁脚本**自身头注**也属声明面（实测再抓一处过腐）。
# 判据：声明面（GateManifests.psd1 / Baselines\*.psd1 / Baselines\README.md / 扫描面内 Check-*.ps1）出现的
#   `…\Baselines\<X>.psd1` 必须存在；裸形 `Baselines\<X>.psd1` 先按仓库根解析、再按规范根解析。
$declSurfaces = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $manifestsPath) { $declSurfaces.Add($manifestsPath) }
foreach ($bf in $baselineFiles) { $declSurfaces.Add($bf.FullName) }
foreach ($s in $scripts) { $declSurfaces.Add($s.FullName) }
$baselinesReadme = Join-Path $Root 'Frame\PowerShellScript\Baselines\README.md'
if (Test-Path -LiteralPath $baselinesReadme) { $declSurfaces.Add($baselinesReadme) }

$rxBaselineRef = '(?<full>[A-Za-z0-9_\-\.]+(?:\\[A-Za-z0-9_\-\.]+)*\\[Bb]aselines\\[A-Za-z0-9_\-]+\.psd1)'
foreach ($surface in $declSurfaces)
{
    if (-not (Test-Path -LiteralPath $surface)) { continue }
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($surface))
    {
        $lineNo++
        foreach ($m in [regex]::Matches($line, $rxBaselineRef))
        {
            $rel = $m.Groups['full'].Value
            $candidates = @((Join-Path $Root $rel))
            if ($rel -match '^[Bb]aselines\\') { $candidates += (Join-Path $Root (Join-Path 'Frame\PowerShellScript' $rel)) }
            $exists = $false
            foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { $exists = $true; break } }
            if (-not $exists)
            {
                $violations.Add("[U10] $([IO.Path]::GetFileName($surface)):$lineNo 引用的基线路径不存在：$rel（试过：$($candidates -join ' / ')）")
            }
        }
    }
}

# ---------------- U11 行形态唯一（事实行 = @{ Value; Origin; DueWhen }） ----------------
# 动机（SR-2a2 实测）：并根后仍有**两种行形态**并存（`Value` 与 `Baseline`/`RelativePath`）⇒ 消费端读法分裂，
#   同一事实两种表示（SR 目标态的反例：手写表示 = 1）。
# 判据：单件 `<Gate>.psd1` 的每个事实行须含 `Value` 键（结构键除外）；违规即红。
foreach ($bf in $baselineFiles)
{
    $gateName = [IO.Path]::GetFileNameWithoutExtension($bf.Name)
    $data = Import-PowerShellDataFile -LiteralPath $bf.FullName
    foreach ($k in $data.Keys)
    {
        $keyName = [string]$k
        if ($keyName -in $structuralKeys) { continue }
        $value = $data[$k]
        $rows = @()
        if ($value -is [System.Collections.IDictionary]) { $rows = @($value) }
        elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) { $rows = @($value) }

        foreach ($row in $rows)
        {
            if ($row -isnot [System.Collections.IDictionary]) { continue }
            if (-not $row.Contains('Value'))
            {
                $found = ($row.Keys | Where-Object { $_ -notin @('Origin', 'DueWhen') }) -join '+'
                $violations.Add("[U11] $gateName.psd1 的 '$keyName' 行形态不唯一（非 Origin/DueWhen 键 = $found）⇒ 事实行须为 @{ Value; Origin; DueWhen }")
                break
            }
        }
    }
}

# ---------------- 汇总 ----------------
Write-Host ""
Write-Host "  扫描面：$($scanRoots.Count) 个根 / $($scripts.Count) 件 Check-*.ps1；内联基线命中 $($inlineHits.Count) 处 / $($hitFiles.Count) 件" -ForegroundColor Gray
Write-Host "  外置基线：$($baselineFiles.Count) 件；清册在册 $($registered.Count) 件" -ForegroundColor Gray

if ($ShowDetail -and $pending.Count -gt 0)
{
    Write-Host ""
    Write-Host "  --- 迁移台账（不拦；只减不增） ---" -ForegroundColor DarkGray
    foreach ($p in $pending) { Write-Host "    - $p" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 门禁基线守卫：0 违规（U1–U11 全绿：内联基线在册 / 事实行类型与形态唯一 / 接线与命名唯一 / 声明面路径存在）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 门禁基线守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
Write-Host "  修复口径：Origin=Derived ⇒ 外置 Baselines\<Gate>.psd1；Origin=Debt ⇒ 登记 _inline-baselines.psd1 并给 DueWhen" -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0

