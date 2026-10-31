#requires -Version 7.0
<#
.SYNOPSIS
    帧事实覆盖率守卫（FrameFactCoverage）—— 终答事实必须由**全部帧写入者**落库，读侧才可删除兜底。

.DESCRIPTION
    背景（P4b 前置，R1）：`terminal` 事实曾只由 canon 一条写路径落库，读侧却只有位置兜底；
    先删兜底 ⇒ 答案整块消失（已发生一次）。本门禁把「写入者枚举 ⇄ 事实字段存在性」变成结构判据。

    判据（载体 = 源码结构事实；注释不参与）：
      C1 写入者文件在位（canon / productions / observability）。
      C2 写入者的帧字段构造方法在位（方法名 + 花括号配对提取）。
      C3 该方法体内含事实键 `MessageFrameFacts.TerminalField`。
      C4 `MessageStreamRegistry.(Begin|For)(` 的写入文件集合 == 登记集合（未登记新写入者 ⇒ 红）。
      C5 `observability.Append(` 的写入文件集合 == 登记集合。
      NC 负对照：合成「缺事实键」方法必红；含事实键必绿；`TryGet` 不得被当作写入站点。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 自证判据可判红。
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
    throw '[FrameFactCoverage] 无法定位仓库根（上溯未找到同时含 Frame\ 与 Projects\ 的目录）'
}

$root = Find-RepoRoot
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

# 写入者登记（单一生产者：新增帧写入者必须在此登记，否则 C4/C5 判红）。
$writers = @(
    [pscustomobject]@{
        Id = 'canon'
        File = 'Projects/Plug/Agent/src/Agent.Ui/Ui/Views/Declaration/ConversationCanonGatherModule.Write.cs'
        Method = 'CanonFrameFields'
        Token = 'MessageFrameFacts.TerminalField'
    }
    [pscustomobject]@{
        Id = 'productions'
        File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Productions/ProductionsOpsFunctions.cs'
        Method = 'FrameFields'
        Token = 'MessageFrameFacts.TerminalField'
    }
    [pscustomobject]@{
        Id = 'observability'
        File = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Signal/ProcessNodes.cs'
        Method = 'FrameFieldsWithFacts'
        Token = 'MessageFrameFacts.TerminalField'
    }
)

# 方法体提取（签名 → 首个 `{` → 花括号配对；表达式体方法取到初始化器闭合处，事实键仍在其中）。
function Get-MethodBody([string]$text, [string]$method) {
    $m = [regex]::Match($text, '(?m)^\s*(?:public|private|internal|protected)[^\r\n]*\b' + [regex]::Escape($method) + '\s*\(')
    if (-not $m.Success) { return $null }
    $start = $text.IndexOf('{', $m.Index)
    if ($start -lt 0) { return $null }
    $depth = 0
    for ($i = $start; $i -lt $text.Length; $i++) {
        if ($text[$i] -eq '{') { $depth++ }
        elseif ($text[$i] -eq '}') {
            $depth--
            if ($depth -eq 0) { return $text.Substring($start, $i - $start + 1) }
        }
    }
    return $null
}

function Test-FactCoverage([string]$text, [string]$method, [string]$token) {
    $body = Get-MethodBody $text $method
    return ($null -ne $body) -and $body.Contains($token)
}

function Test-MessageWriterSite([string]$text) {
    return [regex]::IsMatch($text, 'MessageStreamRegistry\.(?:Begin|For)\(')
}

function Get-RelativePath([string]$absolute, [string]$repo) {
    $rel = $absolute.Substring($repo.Length).TrimStart('\', '/')
    return ($rel -replace '\\', '/')
}

# ---- C1–C3：登记写入者 ⇄ 帧字段构造方法含事实键 ----
foreach ($w in $writers) {
    $path = Join-Path $root ($w.File -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Violation 'C1' "写入者文件缺失：$($w.Id) → $($w.File)"
        continue
    }

    $text = Get-Content -LiteralPath $path -Raw
    if (-not (Test-FactCoverage $text $w.Method $w.Token)) {
        Add-Violation 'C2' "帧字段构造方法缺失或未含事实键：$($w.Id) / $($w.Method) / $($w.Token)"
    }
    else {
        Write-Host ("[FrameFactCoverage] {0} {1}.{2} 含 {3}" -f $w.Id, [IO.Path]::GetFileName($w.File), $w.Method, $w.Token)
    }
}

# ---- C4/C5：写入站点集合 == 登记集合（未登记新写入者 ⇒ 红） ----
$sourceRoots = @(
    (Join-Path $root 'Projects\Plug\Agent\src\Agent.Modules'),
    (Join-Path $root 'Projects\Plug\Agent\src\Agent.Ui')
)
$csFiles = @(
    foreach ($r in $sourceRoots) {
        if (-not (Test-Path -LiteralPath $r)) { continue }
        Get-ChildItem -LiteralPath $r -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\' }
    }
)

$msgSites = @($csFiles | Select-String -Pattern 'MessageStreamRegistry\.(?:Begin|For)\(' -ErrorAction SilentlyContinue)
$msgFiles = @($msgSites | ForEach-Object { Get-RelativePath $_.Path $root } | Sort-Object -Unique)
$expectedMsg = @(
    'Projects/Plug/Agent/src/Agent.Modules/Framework/Productions/ProductionsOpsFunctions.cs',
    'Projects/Plug/Agent/src/Agent.Ui/Ui/Views/Declaration/ConversationCanonGatherModule.Write.cs'
)
foreach ($f in $msgFiles) {
    if ($expectedMsg -notcontains $f) { Add-Violation 'C4' "未登记的 message-stream 写入者：$f" }
}
foreach ($e in $expectedMsg) {
    if ($msgFiles -notcontains $e) { Add-Violation 'C4' "登记的 message-stream 写入者缺失：$e" }
}

$obsSites = @($csFiles | Select-String -Pattern 'observability\.Append\(' -ErrorAction SilentlyContinue)
$obsFiles = @($obsSites | ForEach-Object { Get-RelativePath $_.Path $root } | Sort-Object -Unique)
$expectedObs = @('Projects/Plug/Agent/src/Agent.Modules/Framework/Signal/ProcessNodes.cs')
foreach ($f in $obsFiles) {
    if ($expectedObs -notcontains $f) { Add-Violation 'C5' "未登记的 observability 写入者：$f" }
}
foreach ($e in $expectedObs) {
    if ($obsFiles -notcontains $e) { Add-Violation 'C5' "登记的 observability 写入者缺失：$e" }
}

# ---- 负对照（判据自证可判红） ----
if ($NegativeControl) {
    $good = 'private static IReadOnlyDictionary<string, object?> FrameFields(AcceptedProduction item, bool isTerminal) { return new Dictionary<string, object?> { [MessageFrameFacts.TerminalField] = isTerminal }; }'
    $bad = 'private static IReadOnlyDictionary<string, object?> FrameFields(AcceptedProduction item, bool isTerminal) { return new Dictionary<string, object?> { ["mark"] = "None" }; }'
    if (-not (Test-FactCoverage $good 'FrameFields' 'MessageFrameFacts.TerminalField')) {
        Add-Violation 'NC' '正对照未命中：含事实键的合成方法被判缺失'
    }
    if (Test-FactCoverage $bad 'FrameFields' 'MessageFrameFacts.TerminalField') {
        Add-Violation 'NC' '负对照误命中：缺事实键的合成方法被判通过'
    }
    if (-not (Test-MessageWriterSite 'var run = MessageStreamRegistry.For(container);')) {
        Add-Violation 'NC' '正对照未命中：MessageStreamRegistry.For 未被识别为写入站点'
    }
    if (Test-MessageWriterSite 'var run = MessageStreamRegistry.TryGet(container, out var found);') {
        Add-Violation 'NC' '负对照误命中：TryGet 被当作写入站点'
    }
    Write-Host ("[FrameFactCoverage] NC good={0} bad={1}" -f (Test-FactCoverage $good 'FrameFields' 'MessageFrameFacts.TerminalField'), (Test-FactCoverage $bad 'FrameFields' 'MessageFrameFacts.TerminalField'))
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[FrameFactCoverage] 通过（0 违规；写入者集合 = canon/productions/observability）'
    if ($Check) { exit 0 }
}

