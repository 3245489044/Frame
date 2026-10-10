#requires -Version 5.1
<#
.SYNOPSIS
    USharp 记录实例形态守卫（V1 实例形态 / V2 纯三列 / V3 规范形单源 / V9 依赖方向）。

.DESCRIPTION
    判据（-Check 时违规 → 退出码 1）：
      U1 兼容面零残留（FAIL）：Operation\DataStoreDriven\**\*.cs 禁 [IndexJson] / KvJsonTable / KvDocumentTable / ExtractScalar
      U2 纯三列模板（FAIL）：KvUsharpTable.SchemaColumns 列名恰 = {Id,NodeKey,Doc}
      U3 表声明纯三列（FAIL）：Config\nodes\tables.ushar 每个 [Table] 块列名恰 = {Id,NodeKey,Doc}
      U4 规范形单源（FAIL）：记录头渲染字面 "[Record] " 唯一定义于 UsharpRecordDecl；UsharpDocumentCodec 委托 ToCanonical
      U5 存储层零 Usharp.* 依赖（V9 · FAIL）：Operation\DataStoreDriven\**\*.cs 无 'using Usharp.'
      U6 正/负对照（FAIL）：扫描面非空 + 各判据正则能捕获合成样本（防假绿）

    基线：Frame\PowerShellScript\Baselines\UsharpDocForm.psd1（单源，禁内联）
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
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name UsharpDocForm

$dsdDir     = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven'
$usharpDir  = Join-Path $Root 'Frame\Usharp\src'
$kvUsharp   = Join-Path $dsdDir 'Sqlite\KvUsharpTable.cs'
$docCodec   = Join-Path $usharpDir 'Usharp.Adapters.Declarations\UsharpDocumentCodec.cs'
$tablesFile = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\tables.ushar'

$excludeRx    = '\\(obj|bin|BuildOutput|_deprecated|backups)\\'
$violations   = [System.Collections.Generic.List[string]]::new()
$legacyRx     = '\[IndexJson\]|KvJsonTable|KvDocumentTable|ExtractScalar'
$ustarUsingRx = 'using\s+Usharp\.'

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  USharp 记录实例形态守卫（V1/V2/V3-form + V9 依赖方向）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

function Get-SourceFiles([string]$Dir, [string]$Filter) {
    if (-not (Test-Path -LiteralPath $Dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $Dir -Recurse -Filter $Filter -File |
        Where-Object { $_.FullName -notmatch $excludeRx })
}

$dsdFiles = Get-SourceFiles $dsdDir '*.cs'

# ---------------- U1 兼容面零残留 ----------------
$u1 = 0
foreach ($f in $dsdFiles) {
    $no = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $no++
        $t = $line.TrimStart()
        if ($t.StartsWith('//')) { continue }   # 注释/文档注释不计（只判可编译标识符）
        if ($line -match $legacyRx) {
            $u1++
            if ($ShowDetail) { Write-Host ("  [U1] {0}:{1} {2}" -f $f.Name, $no, $line.Trim()) -ForegroundColor DarkGray }
        }
    }
}
if ($u1 -gt [int]$baseline.ForbiddenLegacyTokens.Value) {
    $violations.Add("U1 兼容面残留 $u1 处（禁 [IndexJson]/KvJsonTable/KvDocumentTable/ExtractScalar；基线 $([int]$baseline.ForbiddenLegacyTokens.Value)）")
}

# ---------------- U2 纯三列模板 ----------------
$cols = @()
if (Test-Path -LiteralPath $kvUsharp) {
    $text = [System.IO.File]::ReadAllText($kvUsharp)
    $m = [regex]::Match($text, 'SchemaColumns\s*=\s*\{(?<body>.*?)\}\s*;', 'Singleline')
    if ($m.Success) {
        $cols = @([regex]::Matches($m.Groups['body'].Value, '\[(?<n>[A-Za-z_][A-Za-z0-9_]*)\]') |
            ForEach-Object { $_.Groups['n'].Value } | Select-Object -Unique)
    }
}
$expectedCols = @('Id', 'NodeKey', 'Doc')
$colsOk = ($cols.Count -eq [int]$baseline.PureColumnCount.Value) -and
          (@($expectedCols | Where-Object { $cols -notcontains $_ }).Count -eq 0)
if (-not $colsOk) {
    $violations.Add("U2 KvUsharpTable.SchemaColumns 列集非纯三列：实测 {$($cols -join ',')}，须恰 = {Id,NodeKey,Doc}")
}

# ---------------- U3 表声明纯三列 ----------------
$declaredTables = [System.Collections.Generic.List[object]]::new()
if (Test-Path -LiteralPath $tablesFile) {
    $cur = $null
    foreach ($raw in [System.IO.File]::ReadAllLines($tablesFile)) {
        $line = ($raw -replace '#.*$', '').TrimEnd()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $trim = $line.Trim()
        if ($trim.StartsWith('[Table]')) {
            $name = (($trim -replace '^\[Table\]\s*', '') -split ':')[0].Trim()
            $cur = [pscustomobject]@{ Name = $name; Cols = [System.Collections.Generic.List[string]]::new() }
            $declaredTables.Add($cur)
            continue
        }
        if ($trim.StartsWith('[')) { $cur = $null; continue }
        if ($null -ne $cur) {
            $mm = [regex]::Match($trim, '^(?<c>[A-Za-z_][A-Za-z0-9_]*)\s*:')
            if ($mm.Success) { $cur.Cols.Add($mm.Groups['c'].Value) }
        }
    }
}
if ($declaredTables.Count -lt [int]$baseline.DeclaredTableCount.Value) {
    $violations.Add("U3 tables.ushar 声明表数 $($declaredTables.Count) < 基线 $([int]$baseline.DeclaredTableCount.Value)（声明面被掏空）")
}
foreach ($t in $declaredTables) {
    $set = @($t.Cols | Select-Object -Unique | Sort-Object)
    if (-not ($set.Count -eq 3 -and $set -contains 'Id' -and $set -contains 'NodeKey' -and $set -contains 'Doc')) {
        $violations.Add("U3 [Table] $($t.Name) 列集非纯三列：{$($set -join ',')}")
    }
}

# ---------------- U4 规范形单源 ----------------
$headerRenders = 0
foreach ($f in (Get-SourceFiles $usharpDir '*.cs')) {
    if ([System.IO.File]::ReadAllText($f.FullName) -match '"\[Record\] ') { $headerRenders++ }
}
if ($headerRenders -ne [int]$baseline.RecordHeaderRenders.Value) {
    $violations.Add("U4 记录头渲染定义数 $headerRenders != 基线 $([int]$baseline.RecordHeaderRenders.Value)（单源 = UsharpRecordDecl.ToCanonical）")
}
if (Test-Path -LiteralPath $docCodec) {
    $codecText = [System.IO.File]::ReadAllText($docCodec)
    if (-not ($codecText -match 'new UsharpRecordDecl\(' -and $codecText -match '\.ToCanonical\(\)')) {
        $violations.Add('U4 UsharpDocumentCodec.SerializeRecord 未委托 UsharpRecordDecl.ToCanonical（规范形单源被绕开）')
    }
}

# ---------------- U5 存储层零 Usharp.* 依赖（V9） ----------------
$u5 = 0
foreach ($f in $dsdFiles) {
    if ([System.IO.File]::ReadAllText($f.FullName) -match $ustarUsingRx) {
        $u5++
        if ($ShowDetail) { Write-Host ("  [U5] {0} 引 Usharp.*" -f $f.Name) -ForegroundColor DarkGray }
    }
}
if ($u5 -gt [int]$baseline.StorageUsharpRefs.Value) {
    $violations.Add("U5 存储层引 Usharp.* 的文件数 $u5 > 基线 $([int]$baseline.StorageUsharpRefs.Value)（依赖方向：仅中立 Plate.DslMethod）")
}

# ---------------- U7 存储层 JSON 出口白名单（U-9/U-11） ----------------
# 纪律：存储层内 JSON 仅存在于「外部兼容读出 / 调试渲染 / 强类型序列化门户 / 测试工具面」白名单；
# 其余 JsonSerializer/JsonDocument/JsonElement 直接使用须归零（注释不计，只判可编译标识符）。
$jsonTokens = 'System\.Text\.Json|JsonSerializer|JsonDocument|JsonElement'
# 允许的 JSON 出口（逐条登记；新增白名单须随批登记）：
#   UsharpValueJson.cs        = 值模型 ⟷ JSON 桥（唯一对外 JSON 出口）
#   JsonDocumentCodec.cs      = 文档编解码 JSON 回退面
#   SqliteTaskPoolStore.cs    = 兼容参数 JsonSerializerOptions（历史装配签名，记录实例路径不经 STJ）
#   ReferenceGraphCoordinator.cs = 图库诊断渲染（Serialize(d) 供 UI，非真源）
#   PersistenceJson.cs        = Core 强类型序列化门户（checkpoint/关系元数据）
#   ReferenceTypeRegistry.cs  = 编译产物反序列化（测试/工具面；生产入口 = DeclaredReferenceVocabulary）
#   TaskPoolStoreFactory.cs   = 工厂透传 JsonSerializerOptions（同 SqliteTaskPoolStore 兼容参数）
$jsonWhitelist = @('UsharpValueJson.cs', 'JsonDocumentCodec.cs', 'SqliteTaskPoolStore.cs', 'ReferenceGraphCoordinator.cs',
                   'PersistenceJson.cs', 'ReferenceTypeRegistry.cs', 'TaskPoolStoreFactory.cs')
$u7 = 0
foreach ($f in $dsdFiles) {
    if ($jsonWhitelist -contains $f.Name) { continue }
    $no = 0
    foreach ($line in [System.IO.File]::ReadAllLines($f.FullName)) {
        $no++
        $t = $line.TrimStart()
        if ($t.StartsWith('//')) { continue }   # 注释/文档注释不计（只判可编译标识符）
        if ($line -match $jsonTokens) {
            $u7++
            if ($ShowDetail) { Write-Host ("  [U7] {0}:{1} 存储层 JSON 直接使用（白名单外）" -f $f.Name, $no) -ForegroundColor DarkGray }
        }
    }
}
if ([bool]$baseline.JsonExitEnforce.Value -and $u7 -gt 0) {
    $violations.Add("U7 存储层 JSON 直接使用（白名单外）$u7 处（U-9/U-11：JSON 仅存在于外部兼容读出/调试渲染）")
}

# ---------------- U6 正/负对照（防假绿） ----------------
if ($dsdFiles.Count -lt 10) { $violations.Add("U6 正对照失效：扫描面仅 $($dsdFiles.Count) 件 .cs（判据无标的）") }
if (-not ('"[IndexJson] TEXT"' -match $legacyRx)) { $violations.Add('U6 负对照失效：兼容面判据未命中合成样本 (ANTI-FALSE-GREEN)') }
if (-not ('using Usharp.Syntax;' -match $ustarUsingRx)) { $violations.Add('U6 负对照失效：依赖方向判据未命中合成样本 (ANTI-FALSE-GREEN)') }

# ---------------- 汇总 ----------------
if ($ShowDetail) {
    Write-Host ("  [明细] 扫描面 {0} 件 .cs；U1 残留={1}；U2 列={2}；U3 表={3}；U4 头渲染={4}；U5 引 Usharp={5}" -f `
        $dsdFiles.Count, $u1, ($cols -join ','), $declaredTables.Count, $headerRenders, $u5) -ForegroundColor DarkGray
}

foreach ($v in $violations) { Write-Host "  [FAIL] $v" -ForegroundColor Red }

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host ('[OK] USharp 记录实例形态：纯三列 {0} 表 / 零兼容残留 / 规范形单源 / 存储层零 Usharp.* 依赖' -f $declaredTables.Count) -ForegroundColor Green
    exit 0
}

if ($Check) { exit 1 }
exit 0
