#requires -Version 5.1
<#
.SYNOPSIS
    节点域位置派生面生成器（产品侧）：按声明真源重写派生面。

.DESCRIPTION
    单一生产者 = `Projects\Plug\Agent\Config\nodes\*.ushar`（各域 [Domain] → placements → id=；U9 JSON 面已退役）。
    本件只负责**生成**（写产品派生面），不承担门禁判定：
      1. instances\nodes.json 的 placements[] 按声明并集（原名）排序重写；
      2. Config\nodes\placement.ushar 的 [Space] LocPlacement / [Matrix] LocToPlacement 键行按并集（wire 形）重写，
         节头与节间注释原样保留。
    判定面 = Environment\CentralTest\Tools\Gate\Node\Check-DomainWireSingleSource.ps1（只读对账）。

.PARAMETER Root
    仓库根（缺省由本脚本位置上溯五级推导）。
.PARAMETER ShowDetail
    输出声明并集明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\Meta\TextOps.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$nodesDir      = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
$instancesFile = Join-Path $Root 'Projects\Plug\Agent\Config\instances\nodes.json'
$placementFile = Join-Path $nodesDir 'foundation\placement.ushar'

if (-not (Test-Path -LiteralPath $nodesDir)) {
    Write-Host "  [失败] 缺失声明目录: $nodesDir" -ForegroundColor Red
    exit 1
}

# ── 采集：声明并集（唯一真源） ────────────────────────────────────────────────
$union = [System.Collections.Generic.List[string]]::new()
foreach ($file in (Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar' -File -Recurse | Sort-Object Name)) {
    $inPlacements = $false
    $placementsIndent = -1
    foreach ($raw in (Get-Content -LiteralPath $file.FullName)) {
        $line = (Remove-LineComment -Text $raw -Marker '#').TrimEnd()
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

Write-Host ("  声明真源: {0} 域 / 位置并集 {1} 项" -f (Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar' -File -Recurse).Count, $union.Count) -ForegroundColor Gray

function ConvertTo-WireId([string]$id) { return ($id -replace '-', '_').ToLowerInvariant() }

$expectedWire = @($union | ForEach-Object { ConvertTo-WireId $_ } | Sort-Object)

if ($ShowDetail) {
    Write-Host ("  [明细] 声明并集: $($union -join ', ')" ) -ForegroundColor DarkGray
}

# ── 生成 1: instances\nodes.json 的 placements[] ─────────────────────────────
if (-not (Test-Path -LiteralPath $instancesFile)) {
    Write-Host '  [失败] 实例派生面缺失: instances\nodes.json' -ForegroundColor Red
    exit 1
}
$ordered = @($union | Sort-Object)
$json = Get-Content -LiteralPath $instancesFile -Raw
$newBlock = ($ordered | ForEach-Object { '    "' + $_ + '"' }) -join ",`r`n"
$json = [regex]::Replace(
    $json,
    '(?s)("placements"\s*:\s*\[).*?(\])',
    { param($m) $m.Groups[1].Value + "`r`n" + $newBlock + "`r`n  " + $m.Groups[2].Value })
Set-Content -LiteralPath $instancesFile -Value $json -NoNewline -Encoding utf8
Write-Host '  [WRITE] instances\nodes.json placements 已按声明并集重写' -ForegroundColor Yellow

# ── 生成 2: placement.ushar 的 LocPlacement / LocToPlacement 键行 ─────────────
if (-not (Test-Path -LiteralPath $placementFile)) {
    Write-Host '  [失败] 位置反查派生面缺失: placement.ushar' -ForegroundColor Red
    exit 1
}
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

Write-Host ''
Write-Host ("[OK] 派生面已生成（{0} 位置）" -f $union.Count) -ForegroundColor Green
exit 0
