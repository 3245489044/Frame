#requires -Version 5.1
<#
.SYNOPSIS
    USharp ↔ C# 交汇面守卫（J1 消费侧门面化 / J2 JSON 出口单源 / J3 零内联构造 / J4 门面四语义覆盖）。

.DESCRIPTION
    判据（-Check 时违规 → 退出码 1）：
      J1 消费侧 using 收敛（FAIL）：Projects\**\src\**\*.cs 中 'using Plate.DslMethod.Usharp;' 的文件数 ≤ 基线（只减不增）
      J2 JSON 出口单源（FAIL）：同面 'Plate.DslMethod.Usharp.UsharpValueJson' 计数 = 0（须经 UsharpDataPorts）
      J3 零内联构造（棘轮 FAIL）：同面 'new Usharp' 计数 ≤ 基线（只减不增；禁对值模型绕过门面内联构造）
      J4 门面四语义覆盖（FAIL）：UsharpDataPorts 成员覆盖 ① 对象↔值 / ② 值↔文本 / ③ SQL / ④ 声明 四类
      J5 正/负对照（FAIL）：扫描面非空 + 各判据正则能捕获合成样本（防假绿）

    扫描面 E-4 裁定：只对生产面 Projects\**\src\** 强制（Environment\CentralTest\** 测试面直引中立模型不在此判据内）。

    基线：Frame\PowerShellScript\Baselines\UsharpJoint.psd1（单源，禁内联）
    模式：默认报告模式；-Check 校验模式（违规 → 退出码 1）。

.PARAMETER Root
    仓库根（缺省由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UsharpJoint

$projectsDir = Join-Path $Root 'Projects'
$facadeFile  = Join-Path $Root 'Frame\Usharp\src\Usharp.Adapters.Declarations\UsharpDataPorts.cs'

$excludeRx    = '\\(obj|bin|BuildOutput|_deprecated|backups)\\'
$violations   = [System.Collections.Generic.List[string]]::new()
$usingRx      = 'using\s+Plate\.DslMethod\.Usharp\s*;'
$valueJsonRx  = 'Plate\.DslMethod\.Usharp\.UsharpValueJson'
$newUsharpRx  = '\bnew\s+Usharp\w'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  USharp ↔ C# 交汇面守卫（J1/J2/J3-form + J4 门面覆盖）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# 扫描面：Projects 下任意 **\src\** 目录的生产 .cs（E-4：不含测试面）
function Get-ConsumerFiles {
    if (-not (Test-Path -LiteralPath $projectsDir)) { return @() }
    return @(Get-ChildItem -LiteralPath $projectsDir -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch $excludeRx -and $_.FullName -match '\\src\\' })
}

$consumerFiles = Get-ConsumerFiles

# ---------------- J1 消费侧 using 收敛 ----------------
$j1 = 0
foreach ($f in $consumerFiles) {
    if ([System.IO.File]::ReadAllText($f.FullName) -match $usingRx) {
        $j1++
        if ($ShowDetail) { Write-Host ("  [J1] {0} 直引 Plate.DslMethod.Usharp" -f $f.FullName) -ForegroundColor DarkGray }
    }
}
if ($j1 -gt [int]$baseline.ConsumerUsharpUsing.Value) {
    $violations.Add("J1 消费侧直引 Plate.DslMethod.Usharp 的文件数 $j1 > 基线 $([int]$baseline.ConsumerUsharpUsing.Value)（须经 UsharpDataPorts）")
}

# ---------------- J2 JSON 出口单源（零全限定 UsharpValueJson） ----------------
$j2 = 0
foreach ($f in $consumerFiles) {
    $no = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $no++
        if ($line.TrimStart().StartsWith('//')) { continue }
        $n = ([regex]::Matches($line, $valueJsonRx)).Count
        if ($n -gt 0) {
            $j2 += $n
            if ($ShowDetail) { Write-Host ("  [J2] {0}:{1} 直调 UsharpValueJson" -f $f.Name, $no) -ForegroundColor DarkGray }
        }
    }
}
if ($j2 -gt [int]$baseline.ConsumerValueJsonCalls.Value) {
    $violations.Add("J2 消费侧直调 Plate.DslMethod.Usharp.UsharpValueJson $j2 处 > 基线 $([int]$baseline.ConsumerValueJsonCalls.Value)（须经 UsharpDataPorts）")
}

# ---------------- J3 零内联构造（new Usharp*） ----------------
$j3 = 0
foreach ($f in $consumerFiles) {
    $no = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $no++
        if ($line.TrimStart().StartsWith('//')) { continue }
        $n = ([regex]::Matches($line, $newUsharpRx)).Count
        if ($n -gt 0) {
            $j3 += $n
            if ($ShowDetail) { Write-Host ("  [J3] {0}:{1} 内联 new Usharp*" -f $f.Name, $no) -ForegroundColor DarkGray }
        }
    }
}
if ($j3 -gt [int]$baseline.ConsumerNewUsharp.Value) {
    $violations.Add("J3 消费侧内联 new Usharp* $j3 处 > 基线 $([int]$baseline.ConsumerNewUsharp.Value)（须经 UsharpDataPorts）")
}

# ---------------- J4 门面四语义覆盖 ----------------
$covered = 0
if (Test-Path -LiteralPath $facadeFile) {
    $facade = [System.IO.File]::ReadAllText($facadeFile)
    $cls1 = ($facade -match 'ToValue\(') -and ($facade -match 'ToObject<')
    $cls2 = ($facade -match 'FromJson\(') -and ($facade -match 'ToPayloadText\(') -and ($facade -match 'ToJson\(')
    $cls3 = ($facade -match 'EnsureSqlFunctions\(') -and ($facade -match 'SchemaSource\(')
    $cls4 = ($facade -match 'DeclarationStoreAsync\(')
    if ($cls1) { $covered++ }
    if ($cls2) { $covered++ }
    if ($cls3) { $covered++ }
    if ($cls4) { $covered++ }
    if (-not $cls1) { $violations.Add('J4 门面缺 ① 对象↔值 语义口（ToValue / ToObject<T>）') }
    if (-not $cls2) { $violations.Add('J4 门面缺 ② 值↔文本 语义口（FromJson / ToPayloadText / ToJson）') }
    if (-not $cls3) { $violations.Add('J4 门面缺 ③ SQL 下推 语义口（EnsureSqlFunctions / SchemaSource）') }
    if (-not $cls4) { $violations.Add('J4 门面缺 ④ 声明入库 语义口（DeclarationStoreAsync）') }
} else {
    $violations.Add('J4 门面缺件：UsharpDataPorts.cs 不存在（判据无标的）')
}
if ($covered -ne [int]$baseline.FacadeSemanticPorts.Value) {
    $violations.Add("J4 门面语义覆盖 $covered != 基线 $([int]$baseline.FacadeSemanticPorts.Value)")
}

# ---------------- J5 正/负对照（防假绿） ----------------
if ($consumerFiles.Count -lt 10) { $violations.Add("J5 正对照失效：扫描面仅 $($consumerFiles.Count) 件 .cs（判据无标的）") }
if (-not ('using Plate.DslMethod.Usharp;' -match $usingRx)) { $violations.Add('J5 负对照失效：using 判据未命中合成样本 (ANTI-FALSE-GREEN)') }
if (-not ('Plate.DslMethod.Usharp.UsharpValueJson.Parse("{}")' -match $valueJsonRx)) { $violations.Add('J5 负对照失效：JSON 出口判据未命中合成样本 (ANTI-FALSE-GREEN)') }
if (-not ('var v = new UsharpRecord(fields);' -match $newUsharpRx)) { $violations.Add('J5 负对照失效：内联构造判据未命中合成样本 (ANTI-FALSE-GREEN)') }

# ---------------- 汇总 ----------------
if ($ShowDetail) {
    Write-Host ("  [明细] 扫描面 {0} 件 .cs；J1 直引={1}；J2 全限定 JSON={2}；J3 内联构造={3}；J4 覆盖={4}/4" -f `
        $consumerFiles.Count, $j1, $j2, $j3, $covered) -ForegroundColor DarkGray
}

foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Red }

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ('[OK] USharp 交汇面：消费侧零散点 / JSON 出口单源 / 零内联构造 / 门面四语义覆盖（扫描 {0} 件）' -f $consumerFiles.Count) -ForegroundColor Green
    exit 0
}

if ($Check) { exit 1 }
exit 0

