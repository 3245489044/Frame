#requires -Version 5.1
<#
.SYNOPSIS
    节点域接入「聚拢点」守卫（R4 派生面单一生产者 + R5 散弹点棘轮）。

.DESCRIPTION
    单一生产者 = `Projects\Plug\Agent\Config\nodes\*.ushar`（各域 [Domain] → placements → id=；U9 JSON 面已退役）。派生面必须与之一致：

      D1 派生面对账（FAIL）：
         - instances\nodes.json 的 placements[] ≡ 各域声明 placements[].id 并集（缺/多即红）
         - Config\nodes\placement.ushar 的 [Space] LocPlacement 位置行 与 [Matrix] LocToPlacement 行 ≡ 并集（下划线形）
      D2 散弹点棘轮（WARN，报告模式起步；方案 §8 回退口径）：per-domain 形态计数只减不增
         - AgentRuntimeSlotKeys.Node<X>Framework / AgentNodeSpec.<X>SubPath / AgentNodeCarriers.Create<X>
         基线在册：Frame\PowerShellScript\Baselines\DomainWireSingleSource.psd1
      D3 正/负对照（FAIL）：域清单可发现（≥ 基线域数）；合成 per-domain 形态必须被判据捕获
      D4 描述行零 per-domain 常量（FAIL · S5.2）：NodeDomainCatalog 描述行禁直接引用
         AgentNodeSpec.<X>SubPath / AgentRuntimeSlotKeys.Node<X>Framework（走 SubPathOf/SlotKeyOf 派生）；
         附正/负对照。

.PARAMETER Root
    仓库根（缺省由本脚本位置上溯四级推导）。

.PARAMETER Write
    生成模式：仅重写 instances\nodes.json 的 placements[]（按声明并集排序）；placement.ushar 的 [Space] LocPlacement / [Matrix] LocToPlacement 键行（节头与注释原样保留）；默认对账不改写。

.PARAMETER ShowDetail
    输出全部明细（含通过项与棘轮读数）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Write,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$nodesDir      = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
$instancesFile = Join-Path $Root 'Projects\Plug\Agent\Config\instances\nodes.json'
$placementFile = Join-Path $nodesDir 'placement.ushar'
$baselineFile  = Join-Path $Root 'Frame\PowerShellScript\Baselines\DomainWireSingleSource.psd1'
$agentSrcDir   = Join-Path $Root 'Projects\Plug\Agent\src'

# 基线单源 = Baselines\DomainWireSingleSource.psd1（禁内联；守卫 GateBaselines U7）
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force

$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  节点域接入聚拢点守卫（派生面单一生产者 + 散弹点棘轮）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ── 采集：声明并集（唯一真源） ────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $nodesDir)) {
    Write-Host "  [失败] 缺失声明目录: $nodesDir" -ForegroundColor Red
    exit 1
}

$union = [System.Collections.Generic.List[string]]::new()
# 声明真源 = Config\nodes\*.ushar 的 [Domain] → placements → id=（U9：JSON 面已退役）
foreach ($file in (Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar' -File | Sort-Object Name)) {
    $inPlacements = $false
    $placementsIndent = -1
    foreach ($raw in (Get-Content -LiteralPath $file.FullName)) {
        $line = ($raw -replace '#.*$', '').TrimEnd()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $indent = $line.Length - $line.TrimStart().Length
        $trim = $line.Trim()
        if ($trim.StartsWith('[')) { $inPlacements = $false; continue }
        if ($trim -match '^placements$') { $inPlacements = $true; $placementsIndent = $indent; continue }
        if ($inPlacements -and $indent -le $placementsIndent) { $inPlacements = $false }
        if (-not $inPlacements) { continue }
        $m = [regex]::Match($trim, '^id\s*=\s*(?<id>[^\s|]+)')
        if ($m.Success) {
            $id = $m.Groups['id'].Value
            if (-not $union.Contains($id)) { $union.Add($id) }
        }
    }
}

if ($union.Count -eq 0) {
    Write-Host '  [失败] 声明面位置并集为空（判据无标的，禁假绿）' -ForegroundColor Red
    exit 1
}

Write-Host ("  声明真源: {0} 域 / 位置并集 {1} 项" -f (Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar' -File).Count, $union.Count) -ForegroundColor Gray

function ConvertTo-WireId([string]$id) { return ($id -replace '-', '_').ToLowerInvariant() }

$expectedWire = @($union | ForEach-Object { ConvertTo-WireId $_ } | Sort-Object)


# ── D1a: instances\nodes.json 派生面 ─────────────────────────────────────────
if (-not (Test-Path -LiteralPath $instancesFile)) {
    $violations.Add("D1a 实例派生面缺失: instances\nodes.json")
} else {
    $instanceDoc = Get-Content -LiteralPath $instancesFile -Raw | ConvertFrom-Json
    $actual = @(@($instanceDoc.placements) | ForEach-Object { [string]$_ } | Sort-Object)
    $missing = @($union | Where-Object { $actual -notcontains $_ })
    $extra   = @($actual | Where-Object { -not $union.Contains($_) })
    if ($missing.Count -gt 0) { $violations.Add("D1a instances\nodes.json 缺位置声明: [$($missing -join ',')]") }
    if ($extra.Count -gt 0)   { $violations.Add("D1a instances\nodes.json 多位置声明: [$($extra -join ',')]") }

    if ($Write -and ($missing.Count -gt 0 -or $extra.Count -gt 0)) {
        $ordered = @($union | Sort-Object)
        $json = Get-Content -LiteralPath $instancesFile -Raw
        $newBlock = ($ordered | ForEach-Object { '    "' + $_ + '"' }) -join ",`r`n"
        $json = [regex]::Replace(
            $json,
            '(?s)("placements"\s*:\s*\[).*?(\])',
            { param($m) $m.Groups[1].Value + "`r`n" + $newBlock + "`r`n  " + $m.Groups[2].Value })
        Set-Content -LiteralPath $instancesFile -Value $json -NoNewline -Encoding utf8
        Write-Host '  [WRITE] instances\nodes.json placements 已按声明并集重写' -ForegroundColor Yellow
    }
}

# ── D1b: placement.ushar 派生面 ──────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $placementFile)) {
    $violations.Add("D1b 位置反查派生面缺失: placement.ushar")
} else {
    $section = ''
    $locTokens = [System.Collections.Generic.List[string]]::new()
    $matrixLefts = [System.Collections.Generic.List[string]]::new()
    foreach ($raw in (Get-Content -LiteralPath $placementFile)) {
        $line = ($raw -replace '#.*$', '').TrimEnd()
        if ($line -match '^\s*\[') {
            # 段识别须锚定节头（[Matrix] LocToPlacement 的 over=LocPlacement 含子串 LocPlacement，勿用子串判据）
            if ($line -match '\[Matrix\]\s*LocToPlacement') { $section = 'matrix' }
            elseif ($line -match '\[Space\]\s*LocPlacement') { $section = 'loc' }
            else { $section = 'other' }
            continue
        }
        if ($section -eq 'loc' -and $line.Trim()) { $locTokens.Add($line.Trim()) }
        elseif ($section -eq 'matrix' -and $line -match '=') { $matrixLefts.Add(($line -split '=')[0].Trim()) }
    }

    $locSet = @($locTokens | Where-Object { $_ -ne 'agent_round' } | Sort-Object)
    $matrixSet = @($matrixLefts | Sort-Object)
    $missLoc = @($expectedWire | Where-Object { $locSet -notcontains $_ })
    $extraLoc = @($locSet | Where-Object { $expectedWire -notcontains $_ })
    $missMatrix = @($expectedWire | Where-Object { $matrixSet -notcontains $_ })
    $extraMatrix = @($matrixSet | Where-Object { $expectedWire -notcontains $_ })

    if ($missLoc.Count -gt 0) { $violations.Add("D1b placement.ushar LocPlacement 缺位置行: [$($missLoc -join ',')]") }
    if ($extraLoc.Count -gt 0) { $violations.Add("D1b placement.ushar LocPlacement 多位置行: [$($extraLoc -join ',')]") }
    if ($missMatrix.Count -gt 0) { $violations.Add("D1b placement.ushar LocToPlacement 缺位置行: [$($missMatrix -join ',')]") }
    if ($extraMatrix.Count -gt 0) { $violations.Add("D1b placement.ushar LocToPlacement 多位置行: [$($extraMatrix -join ',')]") }

    if ($Write -and ($missLoc.Count -gt 0 -or $extraLoc.Count -gt 0 -or $missMatrix.Count -gt 0 -or $extraMatrix.Count -gt 0)) {
        # 生成 = 声明并集（wire 形）重写节内**键行**：仅替换节头后连续的缩进行，
        # 节间注释与后续节头原样保留（避免整块替换吞掉注释）。
        $text = Get-Content -LiteralPath $placementFile -Raw
        $locBody = ((@('  agent_round') + @($expectedWire | ForEach-Object { '  ' + $_ })) -join "`r`n") + "`r`n"
        $text = [regex]::Replace(
            $text,
            '(?m)^(\[Space\]\s*LocPlacement[^\r\n]*\r?\n)(?:[ \t]+\S[^\r\n]*\r?\n)*',
            { param($m) $m.Groups[1].Value + $locBody })
        $mxBody = ((@($expectedWire | ForEach-Object { '  ' + $_ + ' = agent_round' })) -join "`r`n") + "`r`n"
        $text = [regex]::Replace(
            $text,
            '(?m)^(\[Matrix\]\s*LocToPlacement[^\r\n]*\r?\n)(?:[ \t]+\S[^\r\n]*\r?\n)*',
            { param($m) $m.Groups[1].Value + $mxBody })
        Set-Content -LiteralPath $placementFile -Value $text -NoNewline -Encoding utf8
        Write-Host '  [WRITE] placement.ushar LocPlacement/LocToPlacement 键行已按声明并集重写' -ForegroundColor Yellow
    }
}

# ── D2: 散弹点棘轮（报告模式） ────────────────────────────────────────────────
$counts = [ordered]@{ SlotKeyConstants = 0; SubPathConstants = 0; CarrierFactoryMethods = 0 }
if (Test-Path -LiteralPath $agentSrcDir) {
    $csFiles = Get-ChildItem -LiteralPath $agentSrcDir -Recurse -Filter '*.cs' -File
    $slotNames = [System.Collections.Generic.HashSet[string]]::new()
    $subPathNames = [System.Collections.Generic.HashSet[string]]::new()
    $factoryNames = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($f in $csFiles) {
        $text = Get-Content -LiteralPath $f.FullName -Raw
        foreach ($m in [regex]::Matches($text, 'AgentRuntimeSlotKeys\.Node[A-Za-z0-9]*Framework')) { [void]$slotNames.Add($m.Value) }
        foreach ($m in [regex]::Matches($text, 'AgentNodeSpec\.[A-Za-z0-9]+SubPath')) { [void]$subPathNames.Add($m.Value) }
        foreach ($m in [regex]::Matches($text, 'AgentNodeCarriers\.Create[A-Za-z0-9]*')) { [void]$factoryNames.Add($m.Value) }
    }
    $counts.SlotKeyConstants = $slotNames.Count
    $counts.SubPathConstants = $subPathNames.Count
    $counts.CarrierFactoryMethods = $factoryNames.Count
}

if (Test-Path -LiteralPath $baselineFile) {
    $baseline = Import-GateBaseline -Name DomainWireSingleSource
    foreach ($key in @($counts.Keys)) {
        if ($baseline.ContainsKey($key)) {
            $cap = [int]$baseline[$key].Value
            if ($counts[$key] -gt $cap) {
                $warnings.Add("D2 $key 散弹点上涨: $($counts[$key]) > 基线 $cap（新增域须零 C#：改走派生/声明）")
            }
        }
    }
    if ($baseline.ContainsKey('DomainCount')) {
        $domainFloor = [int]$baseline.DomainCount.Value
        if ($union.Count -lt $domainFloor) {
            $violations.Add("D3 正对照失效: 声明面位置数 $($union.Count) < 基线 $domainFloor（声明面被掏空）")
        }
    }
} else {
    $warnings.Add('D2 棘轮基线缺失: Baselines\DomainWireSingleSource.psd1（报告模式：未启用棘轮）')
}

# ── D3: 负对照（判据必须能捕获合成形态） ──────────────────────────────────────
if (-not [regex]::IsMatch('AgentRuntimeSlotKeys.NodeDummyFramework', 'AgentRuntimeSlotKeys\.Node[A-Za-z0-9]*Framework')) {
    $violations.Add('D3 负对照失效: per-domain 槽键判据未命中合成形态 (ANTI-FALSE-GREEN)')
}
if (-not [regex]::IsMatch('AgentNodeSpec.DummySubPath', 'AgentNodeSpec\.[A-Za-z0-9]+SubPath')) {
    $violations.Add('D3 负对照失效: per-domain 声明路径判据未命中合成形态 (ANTI-FALSE-GREEN)')
}

# ── D4: 描述行零 per-domain 常量（S5.2 · FAIL） ──────────────────────────────
# 判据：域描述行（NodeDomainCatalog.All）不得直接引用 per-domain 常量
#   （AgentNodeSpec.<X>SubPath / AgentRuntimeSlotKeys.Node<X>Framework）—— 一律走派生
#   （SubPathOf / SlotKeyOf）。唯一真源 = Agent.Modules\NodeSpec\NodeDomainCatalog.cs。
# 落点说明：本判据与 S5.2 同义；因 Get-VerifyPlan 分类面只登记 Operation\DSL\Check-<G>.ps1
#   （Modules\Node\ 下改动不可分类），故随本守卫落此（同类先例见施工单 §8.1⑥）。
$descFile = Join-Path $agentSrcDir 'Agent.Modules\NodeSpec\NodeDomainCatalog.cs'
if (Test-Path -LiteralPath $descFile) {
    $descLineNo = 0
    foreach ($descLine in [System.IO.File]::ReadAllLines($descFile)) {
        $descLineNo++
        $t = $descLine.TrimStart()
        if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
        if ($descLine -match 'AgentNodeSpec\.[A-Za-z0-9]+SubPath' -or $descLine -match 'AgentRuntimeSlotKeys\.Node[A-Za-z0-9]*Framework') {
            $violations.Add("D4 描述行引用 per-domain 常量（应走 SubPathOf/SlotKeyOf 派生）: NodeDomainCatalog.cs:$descLineNo $($descLine.Trim())")
        }
    }
} else {
    $warnings.Add('D4 NodeDomainCatalog.cs 未找到（描述行判据无标的）')
}

# D4 负对照：判据必须能捕获合成形态（防假绿）
if (-not [regex]::IsMatch('AgentNodeSpec.DummySubPath', 'AgentNodeSpec\.[A-Za-z0-9]+SubPath')) {
    $violations.Add('D4 负对照失效: per-domain 声明路径判据未命中合成形态 (ANTI-FALSE-GREEN)')
}
if (-not [regex]::IsMatch('AgentRuntimeSlotKeys.NodeDummyFramework', 'AgentRuntimeSlotKeys\.Node[A-Za-z0-9]*Framework')) {
    $violations.Add('D4 负对照失效: per-domain 槽键常量判据未命中合成形态 (ANTI-FALSE-GREEN)')
}

# ── 输出 ─────────────────────────────────────────────────────────────────────
if ($ShowDetail) {
    Write-Host ("  [明细] 声明并集: $($union -join ', ')") -ForegroundColor DarkGray
    Write-Host ("  [明细] 散弹点读数: SlotKey={0} SubPath={1} CarrierFactory={2}" -f `
        $counts.SlotKeyConstants, $counts.SubPathConstants, $counts.CarrierFactoryMethods) -ForegroundColor DarkGray
}

foreach ($w in $warnings) { Write-Host "  [WARN] $w" -ForegroundColor Yellow }
foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Red }

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host '校验失败：派生面与声明真源漂移（单一生产者 = Config\nodes\*.json）。' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host ("[OK] 派生面 ≡ 声明真源（{0} 位置）；散弹点棘轮告警 {1} 条" -f $union.Count, $warnings.Count) -ForegroundColor Green
exit 0
