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
$gatherPath      = Join-Path $root 'Projects\Plug\Agent\Config\nodes\gather\node-gather.ushar'
$kindsCsPath     = Join-Path $root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\RecognizedKinds.cs'

$allowedClass  = @('message', 'text', 'tool', 'step', 'telemetry', 'terminal', 'accounting', 'notice')
$allowedAnchor = @('always', 'if-answer', 'never')
$allowedFold   = @('none', 'round', 'lead', 'attach', 'group')
# 视图层折叠口径（ui-regions）是呈现层默认态，词域比帧级 fold 宽（含 round-closed）。
$allowedViewFold = @('none', 'round', 'lead', 'attach', 'group', 'round-closed')

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

function Get-GatherDomains([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    $text = Get-Content -LiteralPath $path -Raw
    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($text, '(?m)^\[Gather\]\s+(\S+)')) { $names.Add($m.Groups[1].Value) }
    return @($names | Sort-Object -Unique)
}

# 跨表对账（F9–F12）：帧视图 ⊆ ui 视图 / ui 折叠词 / 读路径域 ⊆ [Gather] / 视图内容域非空且两两不同。
function Test-CrossTable($schema, $readPaths, $uiRegions, $gatherDomains) {
    $errors = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $schema -or $null -eq $readPaths -or $null -eq $uiRegions) {
        $errors.Add('[F9] 跨表对账输入缺失（frame-kinds / read-paths / ui-regions 三者齐备才可判）')
        return $errors
    }

    $rows = @($schema.kinds)
    $views = @($uiRegions.views)
    $viewNames = @($views | ForEach-Object { [string]$_.view })

    foreach ($row in $rows) {
        foreach ($v in @($row.views)) {
            if ($viewNames -notcontains [string]$v) {
                $errors.Add(("[F9] 帧 {0} 声明出现于视图 '{1}'，但 ui-regions.views 未声明" -f $row.kind, $v))
            }
        }
    }

    foreach ($view in $views) {
        $fold = [string]$view.content.fold
        if ($allowedViewFold -notcontains $fold) {
            $errors.Add(("[F10] 视图 {0} 的 content.fold='{1}' 非受控词" -f $view.view, $fold))
        }

        if ([string]::IsNullOrWhiteSpace([string]$view.content.domain)) {
            $errors.Add(("[F12] 视图 {0} 缺 content.domain（内容域单源）" -f $view.view))
        }
    }

    # F12：视图内容域两两不同（共用同一数据身份 = 双源）。
    $declaredDomains = @($views | ForEach-Object { [string]$_.content.domain } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($g in @($declaredDomains | Group-Object | Where-Object { $_.Count -gt 1 })) {
        $errors.Add(("[F12] 视图内容域重复（须两两不同）：'{0}' 出现 {1} 次" -f $g.Name, $g.Count))
    }

    foreach ($path in @($readPaths.paths)) {
        $d = [string]$path.domain
        if ([string]::IsNullOrWhiteSpace($d)) { $errors.Add(("[F11] 读路径 {0} 缺 domain" -f $path.id)); continue }
        if ($gatherDomains -notcontains $d) {
            $errors.Add(("[F11] 读路径域 '{0}' 未在 [Gather] 声明（path={1}）" -f $d, $path.id))
        }
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

    # ---- 跨表对账（P0：F9–F12；声明文本 → 表之前的结构闭环） ----
    $readPathsPath = Join-Path $root 'Projects\Plug\Agent\Config\nodes\read-paths.json'
    $uiRegionsPath = Join-Path $root 'Projects\Plug\Agent\Config\ui\ui-regions.json'
    $readPathsDecl = if (Test-Path -LiteralPath $readPathsPath) { Get-Content -LiteralPath $readPathsPath -Raw | ConvertFrom-Json -Depth 12 } else { $null }
    $uiRegionsDecl = if (Test-Path -LiteralPath $uiRegionsPath) { Get-Content -LiteralPath $uiRegionsPath -Raw | ConvertFrom-Json -Depth 12 } else { $null }
    $gatherDomains = @(Get-GatherDomains $gatherPath)
    foreach ($e in (Test-CrossTable $schema $readPathsDecl $uiRegionsDecl $gatherDomains)) { Add-Violation 'X' $e }
    Write-Host ("[FrameSchema] 跨表：readPaths={0} uiViews={1} gatherDomains={2}" -f `
        (@($readPathsDecl.paths).Count), (@($uiRegionsDecl.views).Count), $gatherDomains.Count)

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

    # 跨表负对照（F9–F12：帧视图悬空 / 折叠词越集 / 读路径域未进 [Gather] / 内容域不可解释）
    $badSchema = [pscustomobject]@{ kinds = @([pscustomobject]@{ kind = 'a'; views = @('nope') }) }
    $badUi = [pscustomobject]@{ views = @(
        [pscustomobject]@{ view = 'task'; content = [pscustomobject]@{ domain = 'same'; fold = 'sideways' } },
        [pscustomobject]@{ view = 'node'; content = [pscustomobject]@{ domain = 'same'; fold = 'none' } }
    ) }
    $badRp = [pscustomobject]@{ paths = @([pscustomobject]@{ id = 'p'; domain = 'ghost' }) }
    $crossErrors = Test-CrossTable $badSchema $badRp $badUi @('task')
    $crossCodes = @($crossErrors | ForEach-Object { ($_ -split ']')[0].TrimStart('[') })
    foreach ($expected in @('F9', 'F10', 'F11', 'F12')) {
        if ($crossCodes -notcontains $expected) { Add-Violation 'NC' "跨表负对照未命中：期望判据 $expected 未触发" }
    }

    Write-Host '[FrameSchema] 跨表负对照通过（F9/F10/F11/F12 均可判红）'
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[FrameSchema] 通过（0 违规；kind 闭合 + parity + 锚点语义 + 序数单值 + 答案单源）'
if ($Check) { exit 0 }
