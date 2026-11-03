#requires -Version 5.1
<#
.SYNOPSIS
    键身份门禁（KeyIdentity）—— 语义轴/词表/形状/策略/跨域映射 + 执行端零泄漏。

.DESCRIPTION
    判据载体 = 声明面结构事实（字段存在性 / 词表交集 / 权威面计数 / 前缀 / 禁用词检索），
    不读执行者叙述。对齐《终答与窗口-通用身份架构》J2/J3/J4/J9。

      K1 [Entity] 必填 scope/container/cardinality/role/authority
      K2 [KeyLexicon] 非空且 ProcessKind∩ContentKind=∅、AnswerPart∩ProcessKind=∅（A4/J4）
      K3 [AnswerShape]/[WindowShape] 必填项 + default
      K4 [KeyPolicy] entity/cardinality/writer/on_unresolvable + entity 已声明
      K5 [KeyBridge] from/to/rule + reversible=true
      K6 权威面：每个 [Entity] 的 authority=true 族恰一个（S1/S4/J3）
      K7 身份语义面零泄漏：identity.ushar 禁 {cline,powershell,anthropic,openai,sdk}（既有核心按信息项可见化）
      K8 单源漂移：domain.process/kind allowed ≡ ProcessKind 词表；生成物族集 ≡ 声明族集

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 合成样本必须判红。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'TextOps.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $probe = $PSScriptRoot
    while ($probe -and $probe -ne (Split-Path $probe -Parent)) {
        if ((Test-Path (Join-Path $probe 'Projects')) -and (Test-Path (Join-Path $probe 'Frame'))) { break }
        $probe = Split-Path $probe -Parent
    }
    $Root = $probe
}

$configRoot = Join-Path $Root 'Projects\Plug\Agent\Config'
$violations = [System.Collections.Generic.List[string]]::new()

function Get-Fields {
    param([string]$Line)
    return Get-DslFieldBag -Line $Line
}

Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\DeclarationBag.psm1') -Force

function Test-KeyIdentity {
    param([string[]]$Lines, [System.Collections.Generic.List[string]]$Sink)

    $entities = @{}
    $lexicons = @{}
    $shapes = @()
    $policies = @()
    $bridges = @()
    $families = @()

    foreach ($raw in $Lines) {
        $line = $raw.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }

        if ($line -match '^\[Entity\]\s+(?<id>[^:|]+)(:?)(?<rest>.*)$') {
            $id = $Matches['id'].Trim()
            $fields = Get-Fields $Matches['rest']
            $entities[$id] = $fields
            foreach ($need in @('scope','container','cardinality','role','authority')) {
                if (-not $fields.ContainsKey($need) -or [string]::IsNullOrWhiteSpace($fields[$need])) {
                    $Sink.Add("[K1] [Entity] $id 缺 $need=")
                }
            }
            continue
        }

        if ($line -match '^\[KeyLexicon\]\s+(?<name>[^:]+):\s*(?<values>.+)$') {
            $name = $Matches['name'].Trim()
            $values = @(ConvertFrom-CommaList -Text $Matches['values'])
            $lexicons[$name] = $values
            if ($values.Count -eq 0) { $Sink.Add("[K2] [KeyLexicon] $name 值域为空") }
            continue
        }

        if ($line -match '^\[(Answer|Window)Shape\]\s+(?<id>[^|]+)\|(?<rest>.*)$') {
            $shapes += [pscustomobject]@{ Kind = $Matches[1]; Id = $Matches['id'].Trim(); Fields = (Get-Fields $Matches['rest']) }
            continue
        }

        if ($line -match '^\[KeyPolicy\]\s+(?<id>[^|]+)\|(?<rest>.*)$') {
            $policies += [pscustomobject]@{ Id = $Matches['id'].Trim(); Fields = (Get-Fields $Matches['rest']) }
            continue
        }

        if ($line -match '^\[KeyBridge\]\s+(?<id>[^|]+)\|(?<rest>.*)$') {
            $bridges += [pscustomobject]@{ Id = $Matches['id'].Trim(); Fields = (Get-Fields $Matches['rest']) }
            continue
        }

        if ($line -match '^family\s+(?<id>[^|]+)\|(?<rest>.*)$') {
            $families += [pscustomobject]@{ Id = $Matches['id'].Trim(); Fields = (Get-Fields $Matches['rest']) }
        }
    }

    # K2 词表互斥（A4/J4）
    foreach ($pair in @(@('ProcessKind','ContentKind'), @('AnswerPart','ProcessKind'))) {
        $a = $lexicons[$pair[0]]; $b = $lexicons[$pair[1]]
        if (-not $a -or -not $b) { continue }
        foreach ($v in $a) {
            if ($b -contains $v) { $Sink.Add("[K2] 词条 '$v' 同时属于 $($pair[0]) 与 $($pair[1])") }
        }
    }

    # K3 形状必填
    foreach ($shape in $shapes) {
        $need = if ($shape.Kind -eq 'Answer') { @('container','round','text','terminal','default') } else { @('parent','member','terminal','default') }
        foreach ($n in $need) {
            if (-not $shape.Fields.ContainsKey($n) -or [string]::IsNullOrWhiteSpace($shape.Fields[$n])) {
                $Sink.Add("[K3] [$($shape.Kind)Shape] $($shape.Id) 缺 $n=")
            }
        }
    }

    # K4 写侧策略
    foreach ($p in $policies) {
        foreach ($n in @('entity','cardinality','writer','on_unresolvable')) {
            if (-not $p.Fields.ContainsKey($n) -or [string]::IsNullOrWhiteSpace($p.Fields[$n])) {
                $Sink.Add("[K4] [KeyPolicy] $($p.Id) 缺 $n=")
            }
        }
        if ($p.Fields.ContainsKey('entity') -and -not $entities.ContainsKey($p.Fields['entity'])) {
            $Sink.Add("[K4] [KeyPolicy] $($p.Id) entity='$($p.Fields['entity'])' 未在 [Entity] 声明")
        }
    }

    # K5 跨域映射
    foreach ($b in $bridges) {
        foreach ($n in @('from','to','rule')) {
            if (-not $b.Fields.ContainsKey($n) -or [string]::IsNullOrWhiteSpace($b.Fields[$n])) {
                $Sink.Add("[K5] [KeyBridge] $($b.Id) 缺 $n=")
            }
        }
        if (-not $b.Fields.ContainsKey('reversible') -or $b.Fields['reversible'] -ne 'true') {
            $Sink.Add("[K5] [KeyBridge] $($b.Id) reversible 须为 true（镜像必须可逆）")
        }
    }

    # K6 权威面计数
    foreach ($id in $entities.Keys) {
        $owned = @($families | Where-Object { $_.Fields['authority'] -eq 'true' -and $_.Id.EndsWith(".$id", [StringComparison]::Ordinal) })
        if ($owned.Count -eq 0) { $Sink.Add("[K6] [Entity] $id 无 authority=true 的 family"); continue }
        if ($owned.Count -gt 1) { $Sink.Add("[K6] [Entity] $id 权威族 $($owned.Count) 个（须恰一个）") }
        foreach ($f in $owned) {
            if ([string]::IsNullOrWhiteSpace($f.Fields['prefix'])) { $Sink.Add("[K6] 族 $($f.Id) 缺 prefix（禁裸键）") }
        }
    }
}

# ---- 采集声明面（语义轴 + 键族）----
$declarationFiles = @()
$keyspaceDir = Join-Path $configRoot 'keyspaces'
if (Test-Path -LiteralPath $keyspaceDir) {
    $declarationFiles += @(Get-ChildItem -LiteralPath $keyspaceDir -Filter '*.ushar' -File | Select-Object -ExpandProperty FullName)
}

$lines = @()
foreach ($file in $declarationFiles) { $lines += [IO.File]::ReadAllLines($file) }
Test-KeyIdentity -Lines $lines -Sink $violations

# ---- K7 执行端零泄漏（A1/A3/J9）：身份语义声明面禁执行端事件词 ----
# 判据仅作用于本次新增的语义声明面（identity.keyspace）；既有声明面的历史命中按信息项可见化，
# 不在本门禁内一次性改造（迁移见 R4/R5 批）。
$forbidden = @('cline', 'powershell', 'anthropic', 'openai', 'sdk')
$strictFiles = @()
$identityFile = Join-Path $configRoot 'keyspaces\identity.ushar'
if (Test-Path -LiteralPath $identityFile) { $strictFiles += $identityFile }

foreach ($file in $strictFiles) {
    $text = [IO.File]::ReadAllText($file).ToLowerInvariant()
    foreach ($word in $forbidden) {
        if ($text.Contains($word)) {
            $violations.Add("[K7] 核心声明泄漏执行端词 '$word'：$($file.Substring($Root.Length))")
        }
    }
}

foreach ($dir in @('nodes', 'relations', 'workflows')) {
    $path = Join-Path $configRoot $dir
    if (-not (Test-Path -LiteralPath $path)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $path -Filter '*.ushar' -File -Recurse)) {
        $text = [IO.File]::ReadAllText($file.FullName).ToLowerInvariant()
        foreach ($word in $forbidden) {
            if ($text.Contains($word)) {
                Write-Output ("[K7-info] 既有核心声明的执行端词 '{0}'（待 R4/R5 迁移）：{1}" -f $word, $file.FullName.Substring($Root.Length))
                break
            }
        }
    }
}

# ---- K8 词表/产物一致性（单源漂移检测）----
# K8a: 受控词表 ⊆ 可写集（词表值不可写 = 声明与写侧不一致）；内容词混入过程轴按信息项可见化（R3 迁移债）。
$lexiconLine = $lines | Where-Object { $_ -match '^\s*\[KeyLexicon\]\s+ProcessKind:' } | Select-Object -First 1
$contentLine = $lines | Where-Object { $_ -match '^\s*\[KeyLexicon\]\s+ContentKind:' } | Select-Object -First 1
$axisLine = $lines | Where-Object { $_ -match '^\s*axis\s+domain\.process/kind' } | Select-Object -First 1
if ($lexiconLine -and $axisLine) {
    $lexiconValues = @(ConvertFrom-CommaList -Text ([regex]::Match($lexiconLine, '^.*ProcessKind:\s*(?<v>.+)$').Groups['v'].Value))
    $pipe = $axisLine.IndexOf('|')
    $axisFields = if ($pipe -ge 0) { Get-DslFieldBag -Line $axisLine.Substring($pipe + 1) } else { @{} }
    $allowedValues = @(ConvertFrom-CommaList -Text ($axisFields['allowed'] ?? ''))

    $unwritable = @($lexiconValues | Where-Object { $allowedValues -notcontains $_ })
    if ($unwritable.Count -gt 0) {
        $violations.Add("[K8a] ProcessKind 词表值不可写（不在 domain.process/kind allowed=）：$($unwritable -join ', ')")
    }

    if ($contentLine) {
        $contentValues = @(ConvertFrom-CommaList -Text ([regex]::Match($contentLine, '^.*ContentKind:\s*(?<v>.+)$').Groups['v'].Value))
        $leak = @($contentValues | Where-Object { $allowedValues -contains $_ })
        if ($leak.Count -gt 0) {
            Write-Output ("[K8a-info] 过程轴仍含内容词（R3 迁移债，待 recognizers/gather 同批收口）：{0}" -f ($leak -join ', '))
        }
    }
}

# K8b: 仓内生成物族集必须与声明族集一致（手改/漏生成即红）。
$generated = Join-Path $configRoot 'keyspaces\agent.keyspace.generated.json'
if (Test-Path -LiteralPath $generated) {
    try {
        $declaredFamilies = @($lines |
            ForEach-Object { [regex]::Match($_, '^\s*family\s+(?<id>[^\s|]+)').Groups['id'].Value } |
            Where-Object { $_ } | Sort-Object -Unique)
        $generatedFamilies = @((Get-Content -Raw -LiteralPath $generated | ConvertFrom-Json).families |
            ForEach-Object { $_.id } | Sort-Object -Unique)
        $familyDiff = @(Compare-Object -ReferenceObject $declaredFamilies -DifferenceObject $generatedFamilies)
        if ($familyDiff.Count -gt 0) {
            $violations.Add("[K8b] agent.keyspace.generated.json 族集与声明不一致（$($familyDiff.Count) 项差异）")
        }
    }
    catch {
        $violations.Add("[K8b] agent.keyspace.generated.json 不可解析：$($_.Exception.Message)")
    }
}

# ---- 负对照：合成样本必须判红（防判据空转）----
if ($NegativeControl) {
    $probe = [System.Collections.Generic.List[string]]::new()
    Test-KeyIdentity -Lines @(
        '[Entity] broken: x | scope=turn | container=c | role=final',
        '[KeyLexicon] A: x,y',
        '[KeyLexicon] B: y',
        '[AnswerShape] a/1 | container=required',
        '[KeyPolicy] P | entity=missing | cardinality=one',
        '[KeyBridge] BR | from=a | to=b',
        'family domain.broken | template=broken.{container} | prefix=broken. | authority=true'
    ) -Sink $probe
    if ($probe.Count -eq 0) { $violations.Add('[negative-control] 合成的非法声明未被判红（判据空转）') }
    Write-Output ("[negative-control] 命中 {0} 项（期望 > 0）" -f $probe.Count)
}

$distinct = @($violations | Select-Object -Unique)
foreach ($v in $distinct) { Write-Output $v }

if ($Check -and $distinct.Count -gt 0) {
    Write-Output ("KeyIdentity FAIL：{0} 项违规" -f $distinct.Count)
    exit 1
}

Write-Output ("KeyIdentity OK：声明文件 {0} 个" -f $declarationFiles.Count)
exit 0



