#requires -Version 5.1
<#
.SYNOPSIS
    节点位置反解声明守卫（NodeLocPlacement）—— 「位置 = loc 的反解产物，由工作流声明面持有」的机器判据。

.DESCRIPTION
    沿 U12 裁决（Frame\CsharpPrj\Modules\Node\docs\节点通用框架-表层语言与Usharp接入.md §13）：
    位置**不手写声明**（手写即双真源 P22），而由 `loc` 反解派生；反解表本身是声明（`[Matrix]`）。

    扫描面：
      Projects\Plug\Agent\Config\nodes\placement.ushar   （声明面：Loc 词表 / LocPlacement 键空间 / LocToPlacement 反解表）
      Projects\Plug\Agent\Config\nodes\*.json           （位置 id 单源：各域 placements[].id）
      Projects\Plug\Agent\Config\instances\nodes.json   （实例引用面：placements[]）

    判据：
      C1 闸A 编译：声明面经 Usharp.Cli build ⇒ 错误 0（未声明 loc / 悬空引用 / 形状非法即拦）
      C2 键空间完备：LocPlacement 坐标集 = 归一化(位置 id 集) ∪ Loc 词表 —— 双向无游离（漏声明 / 死声明）
      C3 反解唯一：LocToPlacement 行输出**恰覆盖**位置 id 集（每位置恰一行；缺行/多行 ⇒ 违规，对齐 L-2 禁择一）
      C4 正对照（防假绿）：位置声明数 ≥ 基线（声明被删而门禁恒绿 = 失效）
      C5 负对照（防假绿）：U12 §13.2 原样形态（loc 空间 + 位置子空间 + over=Loc）必须报 US1003 ∧ US1009
                          —— 既锁死「该形态不可表达」的实测结论，也证明编译器确实在跑（非仅静态读数）

    标识符约束：坐标名 = [A-Za-z_][A-Za-z0-9_]*（禁 `-`，与线性表达式减号同形，见 UsharpIdentifier）
    ⇒ 声明面用下划线形（archive_raw），与 kebab 形（archive-raw）之归一是本脚本 C2/C3 的比对口径。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（C1–C5）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-NodeLocPlacement.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodeLocPlacement.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$declarationFile = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\placement.ushar'
$nodesDir        = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
$instanceFile    = Join-Path $Root 'Projects\Plug\Agent\Config\instances\nodes.json'
$cliExe          = Join-Path $Root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
$cliProj         = Join-Path $Root 'Frame\Usharp\src\Usharp.Cli\Usharp.Cli.csproj'

$violations  = [System.Collections.Generic.List[string]]::new()
$pending     = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  节点位置反解声明守卫（U12：位置 = loc 反解，工作流声明面持有）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $declarationFile)) {
    Write-Host "[FAIL] 未找到位置反解声明面：$declarationFile" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------------- 编译取数（唯一实现 = Usharp 语言编译器；本脚本不复制闸A） ----------------
function Invoke-UsharpBuild {
    param([string]$Path)
    if (Test-Path -LiteralPath $cliExe) {
        return (& $cliExe build $Path 2>&1 | Out-String)
    }
    if (Test-Path -LiteralPath $cliProj) {
        return (& dotnet run --project $cliProj -- build $Path 2>&1 | Out-String)
    }
    throw "未找到 Usharp 编译器（$cliExe 或 $cliProj）：闸A 判据 C1/C5 不可判，禁静默通过"
}

# ---------------- 声明解析（坐标 / 矩阵行；缩进块归属即语义） ----------------
function Get-Declaration {
    param([string]$Path)
    $spaces  = @{}
    $matrix  = [ordered]@{}
    $current = ''

    foreach ($raw in (Get-Content -LiteralPath $Path)) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }

        if ($line -match '^\[Space\]\s+(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*:') {
            $current = 'space:' + $Matches['id']
            if (-not $spaces.ContainsKey($Matches['id'])) {
                $spaces[$Matches['id']] = [System.Collections.Generic.List[string]]::new()
            }
            continue
        }
        if ($line -match '^\[Matrix\]\s+(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*:') {
            $current = 'matrix:' + $Matches['id']
            continue
        }
        if ($line.StartsWith('[')) { $current = ''; continue }

        if ($current.StartsWith('space:')) {
            $spaceId = $current.Substring(6)
            $coord = ($line -split '\s*\|\s*')[0]
            $coord = ($coord -split ':')[0].Trim()
            if ($coord -match '^[A-Za-z_][A-Za-z0-9_]*$') { $spaces[$spaceId].Add($coord) }
            continue
        }
        if ($current.StartsWith('matrix:')) {
            $eq = $line.IndexOf('=')
            if ($eq -le 0) { continue }
            $outKey = ($line.Substring(0, $eq)).Trim().TrimEnd("'")
            if ($outKey -match '^[A-Za-z_][A-Za-z0-9_]*$') {
                $matrix[$outKey] = ($line.Substring($eq + 1)).Trim()
            }
            continue
        }
    }

    return [pscustomobject]@{ Spaces = $spaces; Matrix = $matrix }
}

# kebab → underscore（标识符约束的唯一归一；禁第二处换算）
function ConvertTo-Identifier {
    param([string]$Name)
    return ($Name -replace '-', '_')
}

$declaration = Get-Declaration -Path $declarationFile

# ---------------- 位置 id 单源（域声明 + 实例引用） ----------------
$placementIds = [System.Collections.Generic.List[string]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $nodesDir -Filter '*.json' -File -ErrorAction SilentlyContinue)) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($doc.PSObject.Properties.Name -contains 'placements' -and $null -ne $doc.placements) {
        foreach ($p in @($doc.placements)) {
            $id = [string]$p.id
            if ($id) { $placementIds.Add($id) }
        }
    }
}

$instanceIds = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $instanceFile) {
    $instances = Get-Content -LiteralPath $instanceFile -Raw | ConvertFrom-Json
    if ($instances.PSObject.Properties.Name -contains 'placements' -and $null -ne $instances.placements) {
        foreach ($id in @($instances.placements)) { $instanceIds.Add([string]$id) }
    }
}

# ---------------- C4 正对照（防假绿，先判：基线失守则后续判据无意义） ----------------
$baseline = 8   # 2026-09-26 实测：4 域 8 位置（conversation 4 + segment 2 + message 1 + process 1）
if ($placementIds.Count -lt $baseline) {
    $violations.Add("[C4] 位置 id 单源计数 $($placementIds.Count) < 基线 $baseline（域声明被删/未装载 ⇒ 判据失效，禁假绿）")
}

# ---------------- C1 闸A 编译 ----------------
$buildText = ''
try {
    $buildText = Invoke-UsharpBuild -Path $declarationFile
} catch {
    $violations.Add("[C1] 编译器不可用：$($_.Exception.Message)")
}
if ($buildText -match '错误\s*(?<n>\d+)') {
    if ([int]$Matches['n'] -ne 0) {
        $violations.Add("[C1] 位置反解声明面闸A 编译错误 $($Matches['n']) 处（须 0）")
        foreach ($line in ($buildText -split "`n" | Where-Object { $_ -match 'error US' })) {
            $violations.Add("[C1] $($line.Trim())")
        }
    }
} else {
    $violations.Add("[C1] 未取得编译错误计数（编译器输出形态变化 ⇒ 判据失效，禁假绿）")
}

# ---------------- C2 键空间完备（LocPlacement = 位置 id ∪ Loc 词表，双向无游离） ----------------
$locCoords = @($declaration.Spaces['Loc'])
$lpCoords  = @($declaration.Spaces['LocPlacement'])
$positionIds = @($placementIds | ForEach-Object { ConvertTo-Identifier $_ } | Sort-Object -Unique)
$expected  = @(@($positionIds) + $locCoords | Sort-Object -Unique)
$actual    = @($lpCoords | Sort-Object -Unique)

foreach ($id in $expected) {
    if ($actual -notcontains $id) {
        $violations.Add("[C2] LocPlacement 未声明坐标 '$id'（位置 id 或 Loc 词表项缺失 ⇒ 反解必落空 L-5）")
    }
}
foreach ($id in $actual) {
    if ($expected -notcontains $id) {
        $violations.Add("[C2] LocPlacement 声明游离坐标 '$id'（既非位置 id 亦非 Loc 词表项 ⇒ 死声明）")
    }
}
foreach ($id in @($instanceIds | Sort-Object -Unique)) {
    if ($placementIds -notcontains $id) {
        $violations.Add("[C2] 实例清单引用位置 '$id' 不在任何域声明（悬空位置引用）")
    }
}

# ---------------- C3 反解唯一（行输出恰覆盖位置 id，每位置恰一行） ----------------
$rowKeys = @($declaration.Matrix.Keys)
foreach ($id in $positionIds) {
    if ($rowKeys -notcontains $id) {
        $violations.Add("[C3] LocToPlacement 缺行：位置 '$id' 无反解声明（该位置永不可达）")
    }
}
foreach ($key in $rowKeys) {
    if ($expected -notcontains $key) {
        $violations.Add("[C3] LocToPlacement 行 '$key' 输出坐标不在反解域（US1003 同族）")
    }
}
foreach ($dupe in @($rowKeys | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })) {
    $violations.Add("[C3] LocToPlacement 行 '$dupe' 重复（反解非唯一，违 L-2）")
}
foreach ($pair in $declaration.Matrix.GetEnumerator()) {
    $refs = @([regex]::Matches([string]$pair.Value, '[A-Za-z_][A-Za-z0-9_]*') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    foreach ($ref in $refs) {
        if ($locCoords -notcontains $ref) {
            $violations.Add("[C3] LocToPlacement 行 '$($pair.Key)' 引用了非 Loc 词表项 '$ref'（loc 单源 = [Space] Loc）")
        }
    }
}

# ---------------- C5 负对照（U12 §13.2 原样形态必须被拒） ----------------
$negativePath = Join-Path ([System.IO.Path]::GetTempPath()) 'usharp-neg-u12-as-drawn.ushar'
@'
[Space] Loc: kind=keys
  workflow_id

[Space] Placement: kind=keys | contains=Loc:loc_owns_placement
  archive_raw
  session_view

[Matrix] LocToPlacement: over=Loc
  archive_raw = workflow_id
  session_view = workflow_id
'@ | Set-Content -LiteralPath $negativePath -Encoding utf8

$negativeText = ''
try {
    $negativeText = Invoke-UsharpBuild -Path $negativePath
} catch {
    $violations.Add("[C5] 负对照不可执行：$($_.Exception.Message)")
}
if ($negativeText -match '错误\s*(?<n>\d+)') {
    if ([int]$Matches['n'] -eq 0) {
        $violations.Add("[C5] 负对照（U12 §13.2 原样形态）竟编译通过 ⇒ 闸A 判据失效或被绕过（禁假绿）")
    }
} else {
    $violations.Add("[C5] 负对照未取得编译错误计数（禁假绿）")
}
foreach ($code in @('US1003', 'US1009')) {
    if ($negativeText -notmatch $code) {
        $violations.Add("[C5] 负对照未报 $code（应同时报 US1003 与 US1009；编译器行为漂移须复核）")
    }
}
Remove-Item -LiteralPath $negativePath -Force -ErrorAction SilentlyContinue

# ---------------- 汇总 ----------------
Write-Host ""
Write-Host ("  实测：位置 id {0} / Loc 词表 {1} / 反解域坐标 {2} / 反解行 {3}" -f `
    $placementIds.Count, $locCoords.Count, $lpCoords.Count, $rowKeys.Count) -ForegroundColor Gray

if ($ShowDetail) {
    Write-Host ("  Loc 词表     : {0}" -f ($locCoords -join ',')) -ForegroundColor Gray
    Write-Host ("  位置 id 单源 : {0}" -f (($placementIds | Sort-Object -Unique) -join ',')) -ForegroundColor Gray
    Write-Host ("  反解行       : {0}" -f ($rowKeys -join ',')) -ForegroundColor Gray
}

if ($ShowDetail -and $pending.Count -gt 0) {
    Write-Host "[NLP] --- 基线待调整（无新增即通过） ---" -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] 节点位置反解守卫：0 违规（位置由 loc 反解声明持有；反解唯一且覆盖完整）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 节点位置反解守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host "  修复口径：位置 id 单源 = Config\nodes\*.json；声明面 = Config\nodes\placement.ushar；" -ForegroundColor Gray
Write-Host "            位置不得在 C#/JSON 手写声明（U12 §13；手写即双真源 P22）。" -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
