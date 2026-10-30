#requires -Version 7.0
<#
.SYNOPSIS
    UI 位置反解产物生成器（UiLoc）—— ui-loc.ushar ⊕ ui-loc.spec.json → ui-loc.generated.json。

.DESCRIPTION
    唯一合并点（单源）：位置词表只来自 `ui-loc.ushar`（闸A 编译面）；
    呈现属性（模板/样式类/文案/常量/状态矩阵）只来自 `ui-loc.spec.json`（键 ≡ loc）。
    产物形态固定，供 Agent.Ui 以嵌入资源读取（UiLocCatalog 唯一读面）。

    判据（结构事实）：
      G1 每个 loc 在 spec 表内恰有一条属性（禁不可达位置）
      G3 spec 表条目全部被 loc 引用（禁死声明）
    任一不满足 ⇒ 退出码 1（不产出半成品）。

.PARAMETER Ushar
    声明面路径（缺省 = Projects\Plug\Agent\Config\ui\ui-loc.ushar）。

.PARAMETER Spec
    属性面路径（缺省 = Projects\Plug\Agent\Config\ui\ui-loc.spec.json）。

.PARAMETER OutFile
    产处路径（缺省 = Projects\Plug\Agent\Config\ui\ui-loc.generated.json）。

.EXAMPLE
    .\Generate-UiLoc.ps1
#>
[CmdletBinding()]
param(
    [string]$Ushar = '',
    [string]$Spec = '',
    [string]$OutFile = '',
    [switch]$DumpLocs
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$uiDir = Join-Path $root 'Projects\Plug\Agent\Config\ui'
if ([string]::IsNullOrWhiteSpace($Ushar)) { $Ushar = Join-Path $uiDir 'ui-loc.ushar' }
if ([string]::IsNullOrWhiteSpace($Spec)) { $Spec = Join-Path $uiDir 'ui-loc.spec.json' }
if ([string]::IsNullOrWhiteSpace($OutFile)) { $OutFile = Join-Path $uiDir 'ui-loc.generated.json' }

foreach ($p in @($Ushar, $Spec)) {
    if (-not (Test-Path -LiteralPath $p)) { throw "缺件（判据不可判）：$p" }
}

# ---------- 解析 .ushar（单源：只读 [Space] UiLoc 词表；spec id ≡ loc，禁第二份反解表） ----------
$locs = [System.Collections.Generic.List[string]]::new()
$section = ''

foreach ($raw in (Get-Content -LiteralPath $Ushar -Encoding utf8)) {
    $line = $raw.TrimEnd()
    if ($line -match '^\s*#') { continue }
    if ($line -match '^\s*\[Space\]\s+UiLoc\b') { $section = 'loc'; continue }
    if ($line -match '^\s*\[') { $section = ''; continue }
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($section -ne 'loc') { continue }

    $text = $line.Trim()
    if ($text -match '^([A-Za-z_][A-Za-z0-9_]*)$') { $locs.Add($Matches[1]) }
}

if ($locs.Count -eq 0) { throw 'UiLoc 词表为空（声明面缺失或形态非法）' }
$dupes = @($locs | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
if ($dupes.Count -gt 0) { throw ("UiLoc 词表重复坐标：" + ($dupes -join ',')) }

# ---------- 读取 spec 属性面 ----------
$specDoc = Get-Content -LiteralPath $Spec -Raw -Encoding utf8 | ConvertFrom-Json -Depth 32
$specs = [ordered]@{}
foreach ($prop in $specDoc.specs.PSObject.Properties) {
    if ($prop.Name -eq 'spec_end') { continue }
    $specs[$prop.Name] = $prop.Value
}

# ---------- G1/G3（结构判据；不满足即不产出） ----------
$violations = [System.Collections.Generic.List[string]]::new()
$locSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$locs, [System.StringComparer]::Ordinal)
foreach ($loc in $locs) {
    if (-not $specs.Contains($loc)) { $violations.Add("G1 缺属性声明：loc '$loc' 在 ui-loc.spec.json 无条目（该位置永不可达）") }
}
foreach ($key in @($specs.Keys)) {
    if (-not $locSet.Contains($key)) { $violations.Add("G3 死声明：spec '$key' 未被任何 loc 引用") }
}

if ($violations.Count -gt 0) {
    Write-Host "[FAIL] UiLoc 声明不自洽（$($violations.Count) 项），不产出：" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Red }
    exit 1
}

# ---------- 坐标集导出（-DumpLocs：门禁消费本出口，禁门禁自带第二份解析） ----------
if ($DumpLocs) {
    [ordered]@{
        locs    = @($locs)
        specIds = @($specs.Keys)
    } | ConvertTo-Json -Depth 8
    exit 0
}

# ---------- 指纹（声明内容单源） ----------
$sha = [System.Security.Cryptography.SHA256]::Create()
$payload = (Get-Content -LiteralPath $Ushar -Raw -Encoding utf8) + "`n" + (Get-Content -LiteralPath $Spec -Raw -Encoding utf8)
$fingerprint = -join ($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload)) | ForEach-Object { $_.ToString('x2') })

# ---------- 组装产物（声明序；确定性） ----------
$map = [ordered]@{}
foreach ($loc in $locs) { $map[$loc] = @($loc) }

$document = [ordered]@{
    schema      = 'ui-loc-v1'
    fingerprint = $fingerprint
    locs        = @($locs)
    locToSpec   = $map
    specs       = $specs
}

$json = $document | ConvertTo-Json -Depth 32
[System.IO.File]::WriteAllText($OutFile, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host ("[OK] UiLoc 产物已生成：locs={0} specs={1} -> {2}" -f $locs.Count, $specs.Count, $OutFile) -ForegroundColor Green
exit 0
