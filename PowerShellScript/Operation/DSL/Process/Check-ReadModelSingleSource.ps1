#requires -Version 5.1
<#
.SYNOPSIS
    读模型单源门禁（ReadModelSingleSource）—— 读模型声明 ↔ op 绑定 ↔ 投影实现 ↔ 缓存形态 的闭合校验。

.DESCRIPTION
    判据载体 = 结构事实（生成目录 JSON 结构 + 源码静态计数），非执行者叙述。
      R1 声明闭合：op.model ⇒ 存在同名 readModels[].model（悬空引用 ⇒ 红）；每条声明被 ≥1 op 引用（死声明 ⇒ 红）
      R2 缓存单源：Ui/Views/** + Core/Functions/** 的缓存形态计数 ≤ 基线（棘轮只降不升）
      R3 投影单源：IReadModelProjector 实现数 = 声明条数（平行投影 / 未实现 ⇒ 红）
      R4 负对照：三合成样本（悬空 model / 死声明 / 视图缓存字段）必须判红（防假绿）
      R5 版本源可判：version ∈ 词表；Event/GraphMembership 若带 versionProbe 则不得为空串

    说明：`channel=ReadModel` 是命令通道枚举值（node/history/query 等共用），不构成本门禁判据；
    本门禁只认 op 级 `model=`（权威 = 读模型方案 §12 R1 修正口径）。

    模式：默认报告（不设退出码）；-Check 违规 ⇒ 退出码 1；-NegativeControl 跑负对照。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\Process\ReadModelSingleSource.psd1'
if (-not (Test-Path -LiteralPath $baselinePath)) {
    Write-Host ("[ReadModelSingleSource] 缺基线：{0}" -f $baselinePath) -ForegroundColor Red
    exit 2
}
$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath

$catalogRel = [string]$baseline.Catalog.Value
$catalogPath = Join-Path $Root $catalogRel
$viewRoots = @(
    'Projects\Plug\Agent\src\Agent.Ui\Ui\Views',
    'Projects\Plug\Agent\src\Agent.Ui\Core\Functions'
)
$projectorRoots = @(
    'Projects\Plug\Agent\src',
    'Frame\DataCenter\DataCenter.Core',
    'Frame\DataCenter\DataCenter.Protocol'
)
$versionModes = @($baseline.VersionModes.Value)

# ---------------- 判据实现（正/负对照共用同一路径，禁两套口径） ----------------

function Get-CatalogClosure {
    param([Parameter(Mandatory = $true)][string]$Json)

    $doc = $Json | ConvertFrom-Json
    $declared = [ordered]@{}
    if ($doc.PSObject.Properties.Name -contains 'readModels') {
        foreach ($rm in @($doc.readModels)) {
            if ($null -eq $rm) { continue }
            $id = [string]$rm.model
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $declared[$id] = [pscustomobject]@{
                Model   = $id
                Version = [string]$rm.version
                Probe   = [string]$rm.versionProbe
                Used    = $false
            }
        }
    }

    $dangling = [System.Collections.Generic.List[string]]::new()
    foreach ($cmd in @($doc.commands)) {
        if ($null -eq $cmd) { continue }
        foreach ($op in @($cmd.ops)) {
            if ($null -eq $op) { continue }
            $model = [string]$op.model
            if ([string]::IsNullOrWhiteSpace($model)) { continue }
            if ($declared.Contains($model)) { $declared[$model].Used = $true }
            else { $dangling.Add(("{0}.{1} → {2}" -f $cmd.action, $op.op, $model)) }
        }
    }

    $dead = @($declared.Values | Where-Object { -not $_.Used } | ForEach-Object { $_.Model })
    $badVersion = @($declared.Values | Where-Object { $versionModes -notcontains $_.Version } |
        ForEach-Object { "{0}={1}" -f $_.Model, $_.Version })
    $fakeProbe = @($declared.Values |
        Where-Object { ($_.Version -eq 'Event' -or $_.Version -eq 'GraphMembership') -and $null -ne $_.Probe -and $_.Probe -ne '' -and $_.Probe.Trim().Length -eq 0 } |
        ForEach-Object { $_.Model })

    return [pscustomobject]@{
        Schema     = [string]$doc.schema
        Declared   = @($declared.Values)
        Dangling   = @($dangling)
        Dead       = @($dead)
        BadVersion = @($badVersion)
        FakeProbe  = @($fakeProbe)
    }
}

function Measure-ViewCacheSites {
    param([Parameter(Mandatory = $true)][string[]]$Texts)

    $patterns = @(
        'VersionedSnapshotCache',
        '\bTtl\w*\s*=',
        'private\s+(?:readonly\s+)?Dictionary<[^>]+>\s+\w+'
    )
    $count = 0
    foreach ($text in $Texts) {
        foreach ($p in $patterns) {
            $count += [regex]::Matches($text, $p).Count
        }
    }
    return $count
}

function Measure-ProjectorImpls {
    param([Parameter(Mandatory = $true)][string]$Base)

    $count = 0
    foreach ($rel in $projectorRoots) {
        $dir = Join-Path $Base $rel
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue) {
            if ($file.FullName -match '\\(obj|bin)\\' -or $file.FullName -like '*\_deprecated\*') { continue }
            $raw = Get-Content -LiteralPath $file.FullName -Raw
            $text = (($raw -split "`n") | ForEach-Object { $_ -replace '//.*$', '' }) -join "`n"
            $count += [regex]::Matches($text, ':\s*IReadModelProjector\b').Count
        }
    }
    return $count
}

$violations = [System.Collections.Generic.List[string]]::new()

# ---------------- R4 负对照（先跑：判据不得空转） ----------------
if ($NegativeControl) {
    $samples = @(
        @{ Id = 'R4-1 悬空 model'; Kind = 'catalog'
           Json = '{"schema":"ui-commands-v2","commands":[{"action":"x","ops":[{"op":"y","model":"missing.model"}]}],"readModels":[]}' },
        @{ Id = 'R4-2 死声明'; Kind = 'catalog'
           Json = '{"schema":"ui-commands-v2","commands":[],"readModels":[{"model":"dead.model","version":"None"}]}' },
        @{ Id = 'R4-3 视图缓存字段'; Kind = 'view'
           Text = 'private readonly Dictionary<string,int> _snapshot = new();' }
    )
    $missed = [System.Collections.Generic.List[string]]::new()
    foreach ($s in $samples) {
        if ($s.Kind -eq 'catalog') {
            $r = Get-CatalogClosure -Json $s.Json
            $hit = (@($r.Dangling).Count + @($r.Dead).Count) -gt 0
        }
        else {
            $hit = (Measure-ViewCacheSites -Texts @($s.Text)) -gt 0
        }
        if (-not $hit) { $missed.Add($s.Id) }
    }
    if ($missed.Count -gt 0) {
        foreach ($m in $missed) { $violations.Add("[R4] 负对照失效：$m 未被判红（判据空转）") }
    }
    else {
        Write-Host ("[ReadModelSingleSource] 负对照 OK：{0}/{0} 合成样本全部判红" -f $samples.Count) -ForegroundColor Green
    }
}

# ---------------- 实测 ----------------
$declaredCount = 0
$projectorCount = 0
$viewCacheCount = 0
$catalogDiagnostic = $null

if (-not (Test-Path -LiteralPath $catalogPath)) {
    $catalogDiagnostic = "catalog-missing:$catalogRel"
}
else {
    $closure = Get-CatalogClosure -Json (Get-Content -LiteralPath $catalogPath -Raw)
    $declaredCount = @($closure.Declared).Count

    foreach ($d in $closure.Dangling) { $violations.Add("[R1] 悬空 model（op 指向未声明读模型）：$d") }
    foreach ($d in $closure.Dead) { $violations.Add("[R1] 死声明（读模型无任何 op 引用）：$d") }
    foreach ($d in $closure.BadVersion) { $violations.Add("[R5] 版本源不在词表：$d") }
    foreach ($d in $closure.FakeProbe) { $violations.Add("[R5] versionProbe 为空串（假版本）：$d") }

    if ($ShowDetail) {
        Write-Host ("  声明 {0} 条 | schema={1}" -f $declaredCount, $closure.Schema) -ForegroundColor DarkGray
    }
}

$texts = [System.Collections.Generic.List[string]]::new()
foreach ($rel in $viewRoots) {
    $dir = Join-Path $Root $rel
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        $texts.Add((($raw -split "`n") | ForEach-Object { $_ -replace '//.*$', '' }) -join "`n")
    }
}
$viewCacheCount = Measure-ViewCacheSites -Texts @($texts)

$projectorCount = Measure-ProjectorImpls -Base $Root
if ($projectorCount -ne $declaredCount) {
    $violations.Add("[R3] 投影单源不成立：IReadModelProjector 实现 $projectorCount ≠ 声明 $declaredCount")
}

$viewCacheBaseline = [int]$baseline.ViewCacheSites.Value
if ($viewCacheCount -gt $viewCacheBaseline) {
    $violations.Add("[R2] 视图侧缓存形态 $viewCacheCount > 基线 $viewCacheBaseline（缓存只许在框架内，棘轮只降不升）")
}

# ---------------- 报告 ----------------
Write-Host '============================================================'
Write-Host '  ReadModelSingleSource 门禁（声明 ↔ 绑定 ↔ 实现 ↔ 缓存）'
Write-Host '============================================================'
Write-Host ("  生成目录: {0}" -f $catalogRel) -ForegroundColor Gray
Write-Host ("  声明 {0} | 投影实现 {1} | 视图缓存形态 {2}/{3}" -f `
        $declaredCount, $projectorCount, $viewCacheCount, $viewCacheBaseline) -ForegroundColor Gray
if ($null -ne $catalogDiagnostic) { Write-Host ("  ! " + $catalogDiagnostic) -ForegroundColor Yellow }

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] ReadModelSingleSource：0 违规（R1/R2/R3/R5 闭合，负对照见上）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host ("[FAIL] ReadModelSingleSource 命中 {0} 处：" -f $violations.Count) -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host ("  - " + $v) -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
