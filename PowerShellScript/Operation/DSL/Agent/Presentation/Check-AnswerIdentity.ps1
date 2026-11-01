#requires -Version 7.0
<#
.SYNOPSIS
    终答身份守卫（AnswerIdentity）—— 终答事实必须**可达**（能产出 true）且**单写者**，禁止常量 false 假覆盖。

.DESCRIPTION
    背景：`ProcessNodes.FrameFieldsWithFacts` 把 `terminal` 常量写 false，`Check-FrameFactCoverage` 只校验
    「方法体内出现事实键」⇒ 常量 false 也通过，于是「有事实、无答案」且门禁假绿（ANTI-NARRATIVE-COMPLETION）。

    判据（载体 = 源码/声明结构事实；注释不参与）：
      A1 声明在位：`Config\nodes\answer-facts.json` 存在且 enabled=true；authority 非空；
         domains 覆盖 process 与 observability；每域 eligible/select 非空；select 词 ∈ {exact-text,last-non-empty,last}。
      A2 可达性：单写者文件存在且含 `MarkFact(` + `TerminalField, true`（可产出 true 的分支）。
      A3 单写者：全库（Agent.Modules / Agent.Ui）`TerminalField` 的 **true 写入**恰 1 处，且落在单写者文件内。
      A4 接线：端点写事实后调用 `AnswerFactWriter.Close(`（写侧收口点非孤儿）。
      A5 幂等单点：`MarkFact(` 调用点全库 = 1（每轮至多一个 terminal=true 的结构前提）。
      NC 负对照：常量 false 合成体必须被 A2 判据判红；含 true 分支的合成体判绿。

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
    throw '[AnswerIdentity] 无法定位仓库根（上溯未找到同时含 Frame\ 与 Projects\ 的目录）'
}

$root = Find-RepoRoot
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$declPath = Join-Path $root 'Projects\Plug\Agent\Config\nodes\answer-facts.json'
$writerRel = 'Projects/Plug/Agent/src/Agent.Modules/Framework/Signal/AnswerFactWriter.cs'
$writerPath = Join-Path $root ($writerRel -replace '/', '\')
$endpointPath = Join-Path $root 'Projects\Plug\Agent\src\Agent.Modules\Cline\Cline\Endpoints\ClineDaemonEndpoint.cs'
$selectWords = @('exact-text', 'last-non-empty', 'last')
$requiredDomains = @('process', 'observability')

function Test-Reachability([string]$text) {
    return [regex]::IsMatch($text, 'MarkFact\(') -and [regex]::IsMatch($text, 'TerminalField\s*,\s*true')
}

function Test-ConstantOnly([string]$text) {
    return [regex]::IsMatch($text, 'TerminalField\]\s*=\s*false') -and -not (Test-Reachability $text)
}

# ---- A1 声明在位且闭合 ----
if (-not (Test-Path -LiteralPath $declPath)) {
    Add-Violation 'A1' '声明缺失：Config\nodes\answer-facts.json'
}
else {
    $decl = Get-Content -LiteralPath $declPath -Raw | ConvertFrom-Json -Depth 16
    if ($decl.enabled -ne $true) { Add-Violation 'A1' 'answer-facts.enabled 非 true（终答投影被关闭 ⇒ 观测路径答案不可达）' }
    if (@($decl.authority).Count -eq 0) { Add-Violation 'A1' 'answer-facts.authority 为空（未声明可投影的权威级）' }
    $domainIds = @($decl.domains | ForEach-Object { [string]$_.id })
    foreach ($required in $requiredDomains) {
        if ($domainIds -notcontains $required) { Add-Violation 'A1' "answer-facts.domains 缺域 '$required'" }
    }
    foreach ($domain in @($decl.domains)) {
        if (@($domain.eligible).Count -eq 0) { Add-Violation 'A1' "域 '$($domain.id)' 未声明 eligible" }
        if (@($domain.select).Count -eq 0) { Add-Violation 'A1' "域 '$($domain.id)' 未声明 select" }
        foreach ($word in @($domain.select)) {
            if ($selectWords -notcontains [string]$word) { Add-Violation 'A1' "域 '$($domain.id)' 的 select 词 '$word' 未登记（受控词：$($selectWords -join '/')）" }
        }
    }
    Write-Host ("[AnswerIdentity] 声明闭合：domains={0} authority={1}" -f ($domainIds -join ','), (@($decl.authority) -join ','))
}

# ---- A2 可达性 ----
if (-not (Test-Path -LiteralPath $writerPath)) {
    Add-Violation 'A2' "单写者缺失：$writerRel"
}
else {
    $writerText = Get-Content -LiteralPath $writerPath -Raw
    if (-not (Test-Reachability $writerText)) {
        Add-Violation 'A2' '单写者内未找到可产出 terminal=true 的分支（MarkFact + TerminalField, true）'
    }
    else {
        Write-Host '[AnswerIdentity] 可达性：AnswerFactWriter 含 terminal=true 写分支'
    }
}

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

# ---- A3 单写者（每域恰一：登记集 ⇄ 实际写入集双向相等） ----
# 登记表（新增终答写入者必须在此登记，否则判红；两域各一 = 结构面不重叠）。
$registeredWriters = @(
    'Projects/Plug/Agent/src/Agent.Modules/Framework/Signal/AnswerFactWriter.cs',        # obs/proc 帧事实投影
    'Projects/Plug/Agent/src/Agent.Modules/Framework/History/RoundMessageSettlement.cs'  # smsg 归档同拍事实
)
$trueSites = @($csFiles | Select-String -Pattern 'TerminalField\s*,\s*true|TerminalField\]\s*=\s*true' -ErrorAction SilentlyContinue)
$trueFiles = @($trueSites | ForEach-Object { $_.Path.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/' } | Sort-Object -Unique)
foreach ($f in $trueFiles) {
    if ($registeredWriters -notcontains $f) { Add-Violation 'A3' ("未登记的 terminal=true 写入者：{0}" -f $f) }
}
foreach ($e in $registeredWriters) {
    if ($trueFiles -notcontains $e) { Add-Violation 'A3' ("登记的 terminal=true 写入者缺失：{0}" -f $e) }
}

$markSites = @($csFiles | Select-String -Pattern '\bMarkFact\(' -ErrorAction SilentlyContinue)
$markFiles = @($markSites | ForEach-Object { $_.Path.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/' } | Sort-Object -Unique)
if ($markFiles.Count -ne 1 -or $markFiles[0] -ne $writerRel) {
    Add-Violation 'A5' ("MarkFact 调用文件集合异常（须仅单写者）：{0}" -f ($markFiles -join ' | '))
}

# ---- A4 接线 ----
if (-not (Test-Path -LiteralPath $endpointPath)) {
    Add-Violation 'A4' '端点文件缺失：ClineDaemonEndpoint.cs'
}
elseif (-not ((Get-Content -LiteralPath $endpointPath -Raw).Contains('AnswerFactWriter.Close('))) {
    Add-Violation 'A4' '端点未调用 AnswerFactWriter.Close（终答写侧收口点成孤儿）'
}

# ---- A6 非事实源不得写 Terminal 常量（false = 静默假事实） ----
$falseSites = @($csFiles | Select-String -Pattern 'Terminal:\s*false' -ErrorAction SilentlyContinue)
if ($falseSites.Count -gt 0) {
    foreach ($hit in $falseSites) {
        $rel = $hit.Path.Substring($root.Length).TrimStart('\', '/')
        Add-Violation 'A6' ("非事实源写 Terminal: false（须 null = 未知）：{0}:{1}" -f $rel, $hit.LineNumber)
    }
}

# ---- 负对照（判据自证可判红） ----
if ($NegativeControl) {
    $bad = 'private static void FrameFieldsWithFacts() { copy[MessageFrameFacts.TerminalField] = false; }'
    $good = 'public static bool Close(run) { return run.MarkFact(seq, FrameFacts.TerminalField, true); }'
    if (-not (Test-ConstantOnly $bad)) { Add-Violation 'NC' '负对照未命中：常量 terminal=false 合成体被判通过' }
    if (-not (Test-Reachability $good)) { Add-Violation 'NC' '正对照未命中：含 true 分支的合成体被判缺失' }
    Write-Host ("[AnswerIdentity] NC constantOnly={0} reachable={1}" -f (Test-ConstantOnly $bad), (Test-Reachability $good))
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[AnswerIdentity] 通过（0 违规；终答事实可达 + 单写者 + 已接线）'
if ($Check) { exit 0 }
