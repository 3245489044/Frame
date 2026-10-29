#requires -Version 5.1
<#
.SYNOPSIS
    history 语义收拢守卫（HistorySemantics）—— 通用层不留领域语义、不反向依赖 Agent。

.DESCRIPTION
    判据（载体 = 源码符号级静态计数；注释不计入类型/命名空间判据；扫描面 = Frame\CsharpPrj）：
      H1 History 类型声明数（分区三账；正则含 struct ⇒ `readonly record struct XxxHistory*` 不逃逸）：
        H1a Operation / H1b Share / H1c 其余层 ≤ 基线。
      H2 Frame 内 `namespace <...>History` 命名空间文件数 ≤ 基线（0）。
      H3 Frame 内 `using Agent.` 文件数 ≤ 基线（0）—— 通用层不得反向依赖 Agent。
      H4 Frame 内 `Agent*` 类型声明数 ≤ 基线（0）—— 记录/运行态类型全部归 Agent。
      H5 提及 `History` 的文件数（分区 H5a Operation / H5b Share / H5c 其余 / H5d Frame 总量；棘轮，Debt 项；只报不拦）。
      NC 正/负对照：`public sealed class HistoryRecord` 与 `public readonly record struct HistoryKey` 必命中；
        注释文本与 `const string HistoryOwnsTask` 常量行不命中。
      分工：ContainerSemantics C1 只扫 Operation；Frame 全量账（含 Share）由本件承担，禁双写者。

    模式：默认报告；-Check 校验（H1a/H1b/H1c/H2/H3/H4/NC 违规 → 退出码 1；H5x 只报不拦）；
    -List 逐条打印命中（文件:行 + 文本），把「扩面后确有 N 项」变成一次运行可复算的事实。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl, [switch]$List)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Frame\CsharpPrj'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[HistorySemantics] 无法定位仓库根（上溯未找到含 Frame\CsharpPrj 的目录）'
}

$root = Find-RepoRoot
$frameRoot = Join-Path $root 'Frame'
$frameCsRoot = Join-Path $frameRoot 'CsharpPrj'
$opRoot = Join-Path $frameCsRoot 'Operation'
$shareRoot = Join-Path $frameCsRoot 'Share'
$baselinePath = Join-Path $frameRoot 'PowerShellScript\Baselines'
Import-Module (Join-Path $baselinePath 'GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name HistorySemantics

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

function Get-CsFiles([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    Get-ChildItem -LiteralPath $Path -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\' }
}

# 含 struct：`readonly record struct HistoryKey` 曾因缺 struct 逃逸（RF8 缺口）
$historyDeclPattern = '^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*(?:class|interface|record|enum|struct)\s+(?:struct\s+)?\w*History\w*'
$agentDeclPattern = '^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*\b(?:class|interface|record|enum)\s+Agent\w*'

$frameFiles = @(Get-CsFiles $frameCsRoot)
$opFiles = @($frameFiles | Where-Object { $_.FullName -match '\\Operation\\' })
$shareFiles = @($frameFiles | Where-Object { $_.FullName -match '\\Share\\' })
$otherFiles = @($frameFiles | Where-Object { $_.FullName -notmatch '\\Operation\\|\\Share\\' })

function Measure-HistoryDecls([object[]]$Files) {
    $n = 0
    foreach ($f in $Files) {
        $hits = Select-String -LiteralPath $f.FullName -Pattern $historyDeclPattern -AllMatches
        if (-not $hits) { continue }
        $n += @($hits).Count
        if ($List) { foreach ($h in $hits) { Write-Host ("  [decl] {0}:{1} {2}" -f $f.FullName, $h.LineNumber, $h.Line.Trim()) } }
    }
    return $n
}

# H1 History 类型声明（三账：分区计数防「一处降一处升」被总量掩盖）
$h1a = Measure-HistoryDecls $opFiles
if ($h1a -gt ([int]$baseline.OperationHistoryTypeDeclarations.Value)) {
    Add-Violation 'H1a' "Operation 内 *History* 类型声明 = $h1a（基线 $([int]$baseline.OperationHistoryTypeDeclarations.Value)）"
}
$h1b = Measure-HistoryDecls $shareFiles
if ($h1b -gt ([int]$baseline.ShareHistoryTypeDeclarations.Value)) {
    Add-Violation 'H1b' "Share 内 *History* 类型声明 = $h1b（基线 $([int]$baseline.ShareHistoryTypeDeclarations.Value)）"
}
$h1c = Measure-HistoryDecls $otherFiles
if ($h1c -gt ([int]$baseline.OtherFrameHistoryTypeDeclarations.Value)) {
    Add-Violation 'H1c' "Frame 其余层 *History* 类型声明 = $h1c（基线 $([int]$baseline.OtherFrameHistoryTypeDeclarations.Value)）"
}

# H2 History 命名空间（扫描面 = Frame\CsharpPrj）
$h2 = 0
foreach ($f in $frameFiles) {
    if (Select-String -LiteralPath $f.FullName -Pattern '^\s*namespace\s+[\w.]*\bHistory\b' -Quiet) {
        $h2++
        if ($List) { Write-Host "  [ns] $($f.FullName)" }
    }
}
if ($h2 -gt ([int]$baseline.FrameHistoryNamespaceFiles.Value)) {
    Add-Violation 'H2' "Frame 内 History 命名空间文件 = $h2（基线 $([int]$baseline.FrameHistoryNamespaceFiles.Value)）"
}

# H3 层向：Frame 不得 using Agent.*
$h3 = 0
foreach ($f in (Get-CsFiles (Join-Path $frameRoot 'CsharpPrj'))) {
    if (Select-String -LiteralPath $f.FullName -Pattern '^\s*using\s+Agent\.' -Quiet) { $h3++ }
}
if ($h3 -gt ([int]$baseline.FrameUsingAgent.Value)) {
    Add-Violation 'H3' "Frame 内 using Agent.* 文件 = $h3（基线 $([int]$baseline.FrameUsingAgent.Value)）"
}

# H4 Frame 内 Agent* 类型声明（扫描面 = Frame\CsharpPrj）
$h4 = 0
foreach ($f in $frameFiles) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern $agentDeclPattern -AllMatches
    if ($hits) { $h4 += @($hits.Matches).Count }
}
if ($h4 -gt ([int]$baseline.FrameAgentTypeDeclarations.Value)) {
    Add-Violation 'H4' "Operation 内 Agent* 类型声明 = $h4（基线 $([int]$baseline.FrameAgentTypeDeclarations.Value)）"
}

# H5 提及 History 的文件数（分区计数；棘轮，只报不拦）
$mOp = @($opFiles | Select-String -Pattern 'History' -List).Count
$mShare = @($shareFiles | Select-String -Pattern 'History' -List).Count
$mOther = @($otherFiles | Select-String -Pattern 'History' -List).Count
$mFrame = $mOp + $mShare + $mOther
if ($mOp -gt ([int]$baseline.OperationHistoryMentionFiles.Value)) {
    Add-Violation 'H5a' "Operation 提及 History 的文件 = $mOp > 基线 $([int]$baseline.OperationHistoryMentionFiles.Value)（棘轮只降不升）"
}
if ($mShare -gt ([int]$baseline.ShareHistoryMentionFiles.Value)) {
    Add-Violation 'H5b' "Share 提及 History 的文件 = $mShare > 基线 $([int]$baseline.ShareHistoryMentionFiles.Value)（棘轮只降不升）"
}
if ($mOther -gt ([int]$baseline.OtherFrameHistoryMentionFiles.Value)) {
    Add-Violation 'H5c' "Frame 其余层提及 History 的文件 = $mOther > 基线 $([int]$baseline.OtherFrameHistoryMentionFiles.Value)（棘轮只降不升）"
}
if ($mFrame -gt ([int]$baseline.FrameHistoryMentions.Value)) {
    Add-Violation 'H5d' "Frame 提及 History 的文件 = $mFrame > 基线 $([int]$baseline.FrameHistoryMentions.Value)（棘轮只降不升）"
}

# NC 正/负对照
if ($NegativeControl) {
    $pos = [regex]::IsMatch('public sealed class HistoryRecord', $historyDeclPattern)
    $structPos = [regex]::IsMatch('public readonly record struct HistoryKey', $historyDeclPattern)
    $neg = [regex]::IsMatch('// 历史容器（History）注释', $historyDeclPattern)
    $constNeg = [regex]::IsMatch('public const string HistoryOwnsTask = "history_owns_task";', $historyDeclPattern)
    $agentPos = [regex]::IsMatch('public sealed record AgentLoopState(', $agentDeclPattern)
    $excludeNeg = 'X:\Frame\CsharpPrj\Share\obj\AggregateModels.cs' -match '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\'
    $shareIn = 'X:\Frame\CsharpPrj\Share\Frameworks\Conversation\Aggregates\AggregateModels.cs' -notmatch '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\'
    if (-not $pos) { Add-Violation 'NC' '正对照未命中：合成样本 class HistoryRecord 未被 H1 正则捕获' }
    if (-not $structPos) { Add-Violation 'NC' '正对照未命中：合成样本 readonly record struct HistoryKey 未被 H1 正则捕获（struct 缺口）' }
    if ($neg) { Add-Violation 'NC' '负对照误命中：注释文本被判为类型声明' }
    if ($constNeg) { Add-Violation 'NC' '负对照误命中：const 字面量行被判为类型声明' }
    if (-not $agentPos) { Add-Violation 'NC' '正对照未命中：合成样本 record AgentLoopState 未被 H4 正则捕获' }
    if (-not $excludeNeg) { Add-Violation 'NC' '扫描面负对照失效：obj 路径未被排除规则命中' }
    if (-not $shareIn) { Add-Violation 'NC' '扫描面正对照失效：Share 路径被排除规则误排' }
    if ($shareFiles.Count -lt 1) { Add-Violation 'NC' '扫描面无标的：Share 下未枚举到任何 .cs（扩面未生效）' }
    Write-Host "[HistorySemantics] NC pos=$pos structPos=$structPos neg=$neg constNeg=$constNeg agentPos=$agentPos shareFiles=$($shareFiles.Count)"
}

$hard = @($violations | Where-Object { $_ -match '^(H1a|H1b|H1c|H2|H3|H4|NC) ' })
$soft = @($violations | Where-Object { $_ -match '^(H5a|H5b|H5c|H5d) ' })

Write-Host "  H1a Operation *History* 类型声明 = $h1a"
Write-Host "  H1b Share *History* 类型声明     = $h1b"
Write-Host "  H1c Frame 其余层 *History* 类型  = $h1c"
Write-Host "  H2 Frame History 命名空间文件 = $h2"
Write-Host "  H3 Frame using Agent.* 文件 = $h3"
Write-Host "  H4 Frame Agent* 类型声明 = $h4"
Write-Host "  H5a/H5b/H5c/H5d 提及文件 = $mOp/$mShare/$mOther/$mFrame（基线 $([int]$baseline.OperationHistoryMentionFiles.Value)/$([int]$baseline.ShareHistoryMentionFiles.Value)/$([int]$baseline.OtherFrameHistoryMentionFiles.Value)/$([int]$baseline.FrameHistoryMentions.Value)）"

if ($soft.Count -gt 0) {
    Write-Host "[HistorySemantics] 棘轮提示 $($soft.Count) 项：" -ForegroundColor Yellow
    foreach ($w in $soft) { Write-Host "  - $w" -ForegroundColor Yellow }
}
if ($hard.Count -eq 0) {
    Write-Host '[HistorySemantics] 硬判据全过（H1a/H1b/H1c/H2/H3/H4/NC）'
    exit 0
}
Write-Host "[HistorySemantics] 违规 $($hard.Count) 项：" -ForegroundColor Red
foreach ($v in $hard) { Write-Host "  - $v" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
