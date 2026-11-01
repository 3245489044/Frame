#requires -Version 7.0
<#
.SYNOPSIS
    帧语义表守卫（FrameSchema · C 阶段编译视图）—— kind 表必须与既有声明逐项对账，禁成第二权威。

.DESCRIPTION
    背景：帧语义散在 6 处（shaping / presentation / read-paths / ui-regions / recognizers / 代码分支），
    任一缺失都表现为「看起来正常、语义是错的」。C 阶段做法 = 生成**编译视图** `frame-kinds.json`，
    旧声明仍是权威，由本门禁逐项对账（parity），不改行为。

    判据（载体 = 声明文件结构；注释不参与）：
      F1 闭合：frame-kinds 的 kind 集合 ≡ RecognizedKinds.All；无重复行。
      F2 存在性 parity：node-gather.ushar 每个 `shaping <kind>`（排除 `*`）都有对应行。
      F3 呈现词合法：每行 presentation ∈ presentation.json classes。
      F4 锚点语义：anchor=always ⇒ 该呈现类的 surface=bubble（否则气泡承诺落空）。
      F5 序数单值：文件级与行级 ordinal 同为 'arrival'（D4 裁决）。
      F6 答案单源：frame-kinds 内**不得**出现 answer 列（唯一源 = answer-facts.json，D2）。
      F7 预留轴：每行 subject 存在且 = subjectDefault（D5，不进身份键）。
      F8 受控词：class ∈ 固定集；anchor ∈ {always,if-answer,never}；fold ∈ {none,round,lead,attach,group}。
      NC 负对照：重复行 / 非法 presentation / 缺 subject 均判红。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 自证可判红。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and $d -ne (Split-Path $d -Parent)) {
        if ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects'))) { return $d }
        $d = Split-Path $d -Parent
    }
    throw '[FrameSchema] 无法定位仓库根（上溯未找到同时含 Frame\ 与 Projects\ 的目录）'
}

$root = Find-RepoRoot
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$schemaPath      = Join-Path $root 'Projects\Plug\Agent\Config\nodes\frame-kinds.json'
$presentationPath= Join-Path $root 'Projects\Plug\Agent\Config\nodes\presentation.json'
$gatherPath      = Join-Path $root 'Projects\Plug\Agent\Config\nodes\node-gather.ushar'
$kindsCsPath     = Join-Path $root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\RecognizedKinds.cs'

$allowedClass  = @('message', 'text', 'tool', 'step', 'telemetry', 'terminal', 'accounting', 'notice')
$allowedAnchor = @('always', 'if-answer', 'never')
$allowedFold   = @('none', 'round', 'lead', 'attach', 'group')

function Get-RecognizedKinds([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $text = Get-Content -LiteralPath $path -Raw
    $map = @{}
    foreach ($m in [regex]::Matches($text, 'public const string (\w+)\s*=\s*"([^"]+)"')) {
        $map[$m.Groups[1].Value] = $m.Groups[2].Value
    }

    $block = [regex]::Match($text, 'string\[\]\s+All\s*=\s*\{([\s\S]*?)\};')
    if (-not $block.Success) { return @() }

    $result = @()
    foreach ($m in [regex]::Matches($block.Groups[1].Value, '\b([A-Z][A-Za-z0-9_]*)\b')) {
        $name = $m.Groups[1].Value
        if ($map.ContainsKey($name)) { $result += [string]$map[$name] }
    }

    return @($result | Sort-Object -Unique)
}

function Get-ShapingKinds([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $text = Get-Content -LiteralPath $path -Raw

    # 只取**帧域**（声明了帧类别字段 `kind-field` 的 [Gather] 块）；DB 行域（ur_* 等）不是帧类别。
    $kinds = @()
    $blocks = [regex]::Split($text, '(?m)^\[Gather\]\s+')
    foreach ($block in $blocks) {
        if (-not [regex]::IsMatch($block, '(?m)^\s*kind-field\s+\S+')) { continue }
        foreach ($m in [regex]::Matches($block, '(?m)^\s*shaping\s+(\S+)')) {
            $kind = $m.Groups[1].Value
            if ($kind -ne '*') { $kinds += $kind }
        }
    }

    return @($kinds | Sort-Object -Unique)
}

function Test-SchemaRows($kinds, $recognized, $classes, $shapingKinds, $fileOrdinal, $subjectDefault) {
    $errors = [System.Collections.Generic.List[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

    if ($fileOrdinal -ne 'arrival') { $errors.Add(("[F5] 文件级 ordinal='{0}'（须为 arrival）" -f $fileOrdinal)) }

    foreach ($row in @($kinds)) {
        $kind = [string]$row.kind
        if ([string]::IsNullOrWhiteSpace($kind)) { $errors.Add('[F1] 存在空 kind 行'); continue }
        if (-not $seen.Add($kind)) { $errors.Add(("[F1] kind 重复：{0}" -f $kind)) }
        if ($recognized -notcontains $kind) { $errors.Add(("[F1] kind 未在 RecognizedKinds 登记：{0}" -f $kind)) }

        if ($classes -notcontains [string]$row.presentation) { $errors.Add(("[F3] presentation 未在 presentation.json 声明：{0} → {1}" -f $kind, $row.presentation)) }
        if ($allowedClass -notcontains [string]$row.class) { $errors.Add(("[F8] class 非受控词：{0} → {1}" -f $kind, $row.class)) }
        if ($allowedAnchor -notcontains [string]$row.anchor) { $errors.Add(("[F8] anchor 非受控词：{0} → {1}" -f $kind, $row.anchor)) }
        if ($allowedFold -notcontains [string]$row.fold) { $errors.Add(("[F8] fold 非受控词：{0} → {1}" -f $kind, $row.fold)) }
        if ([string]$row.ordinal -ne 'arrival') { $errors.Add(("[F5] 行级 ordinal='{0}'（须与文件级一致）: {1}" -f $row.ordinal, $kind)) }
        if ([string]::IsNullOrWhiteSpace([string]$row.subject) -or [string]$row.subject -ne $subjectDefault) {
            $errors.Add(("[F7] subject 缺失或非缺省值 '{0}'（D5 预留轴）: {1}" -f $subjectDefault, $kind))
        }
        if ($row.PSObject.Properties.Name -contains 'answer') { $errors.Add(("[F6] 出现 answer 列（答案策略唯一源 = answer-facts.json）: {0}" -f $kind)) }
    }

    foreach ($k in @($recognized)) {
        if (-not $seen.Contains($k)) { $errors.Add(("[F1] RecognizedKinds 有而 frame-kinds 缺行：{0}" -f $k)) }
    }

    foreach ($k in @($shapingKinds)) {
        if ($recognized -notcontains $k) { continue }   # DB 行族（ur_*）不是帧类别（帧词表即判据）
        if (-not $seen.Contains($k)) { $errors.Add(("[F2] shaping 声明有而 frame-kinds 缺行：{0}" -f $k)) }
    }

    return $errors
}

# ---- 载入声明面 ----
if (-not (Test-Path -LiteralPath $schemaPath)) { Add-Violation 'F1' '编译视图缺失：Config\nodes\frame-kinds.json' }
if (-not (Test-Path -LiteralPath $presentationPath)) { Add-Violation 'F3' '呈现声明缺失：presentation.json' }

if ((Test-Path -LiteralPath $schemaPath) -and (Test-Path -LiteralPath $presentationPath)) {
    $schema = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json -Depth 12
    $presentation = Get-Content -LiteralPath $presentationPath -Raw | ConvertFrom-Json -Depth 12
    $classes = @($presentation.classes.PSObject.Properties.Name)
    $recognized = @(Get-RecognizedKinds $kindsCsPath)
    $shapingKinds = @(Get-ShapingKinds $gatherPath)

    foreach ($e in (Test-SchemaRows $schema.kinds $recognized $classes $shapingKinds ([string]$schema.ordinal) ([string]$schema.subjectDefault))) {
        Add-Violation 'ROW' $e
    }

    # 行数对账（可见化，非判据）
    Write-Host ("[FrameSchema] 行={0} RecognizedKinds={1} shaping kinds={2}" -f @($schema.kinds).Count, $recognized.Count, $shapingKinds.Count)

    # F4 锚点语义（需要呈现类的 surface）
    foreach ($row in @($schema.kinds)) {
        if ([string]$row.anchor -ne 'always') { continue }
        $spec = $presentation.classes.PSObject.Properties | Where-Object { $_.Name -eq [string]$row.presentation } | Select-Object -First 1
        if ($null -eq $spec) { continue }
        if ([string]$spec.Value.surface -ne 'bubble') {
            Add-Violation 'F4' ("anchor=always 但呈现类 surface≠bubble：{0} → {1}({2})" -f $row.kind, $row.presentation, $spec.Value.surface)
        }
    }
}

# ---- 负对照（判据自证可判红） ----
if ($NegativeControl) {
    $classesOk = @('process', 'notice')
    $recognizedOk = @('a', 'b')
    $shapingOk = @('a', 'b')

    $bad = @(
        [pscustomobject]@{ kind = 'a'; class = 'message'; presentation = 'process'; ordinal = 'arrival'; anchor = 'never'; fold = 'attach'; subject = 'root' },
        [pscustomobject]@{ kind = 'a'; class = 'message'; presentation = 'nope';    ordinal = 'arrival'; anchor = 'never'; fold = 'attach'; subject = 'root' },
        [pscustomobject]@{ kind = 'b'; class = 'message'; presentation = 'process'; ordinal = 'arrival'; anchor = 'never'; fold = 'attach'; subject = '' }
    )
    $badErrors = Test-SchemaRows $bad $recognizedOk $classesOk $shapingOk 'arrival' 'root'
    $codes = @($badErrors | ForEach-Object { ($_ -split ']')[0].TrimStart('[') })
    foreach ($expected in @('F1', 'F3', 'F7')) {
        if ($codes -notcontains $expected) { Add-Violation 'NC' "负对照未命中：期望判据 $expected 未触发" }
    }

    $good = @([pscustomobject]@{ kind = 'a'; class = 'message'; presentation = 'process'; ordinal = 'arrival'; anchor = 'never'; fold = 'attach'; subject = 'root' })
    if ((Test-SchemaRows $good @('a') $classesOk @('a') 'arrival' 'root').Count -ne 0) {
        Add-Violation 'NC' '正对照误报：合法单行被判违规'
    }
    Write-Host '[FrameSchema] 负对照通过（重复/非法呈现词/缺 subject 均可判红）'
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[FrameSchema] 通过（0 违规；kind 闭合 + parity + 锚点语义 + 序数单值 + 答案单源）'
if ($Check) { exit 0 }
