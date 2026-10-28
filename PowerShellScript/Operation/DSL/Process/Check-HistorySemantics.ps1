#requires -Version 5.1
<#
.SYNOPSIS
    history 语义收拢守卫（HistorySemantics）—— 通用层不留领域语义、不反向依赖 Agent。

.DESCRIPTION
    判据（载体 = 源码符号级静态计数；注释不计入类型/命名空间判据）：
      H1 Operation 内 `(class|interface|record|enum) *History*` 类型声明数 ≤ 基线（0）。
      H2 Operation 内 `namespace <...>History` 命名空间文件数 ≤ 基线（0）。
      H3 Frame 内 `using Agent.` 文件数 ≤ 基线（0）—— 通用层不得反向依赖 Agent。
      H4 Operation 内 `Agent*` 类型声明数 ≤ 基线（0）—— 记录/运行态类型全部归 Agent。
      H5 Operation 内提及 `History` 的文件数 ≤ 基线（棘轮，Debt 项；只报不拦）。
      NC 正/负对照：合成 `public sealed class HistoryRecord` 必命中 H1 正则；注释文本不命中。

    模式：默认报告；-Check 校验（H1/H2/H3/H4 违规 → 退出码 1；H5 只报不拦）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Frame\CsharpPrj'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[HistorySemantics] 无法定位仓库根（上溯未找到含 Frame\CsharpPrj 的目录）'
}

$root = Find-RepoRoot
$frameRoot = Join-Path $root 'Frame'
$opRoot = Join-Path $frameRoot 'CsharpPrj\Operation'
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

$historyDeclPattern = '^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*\b(?:class|interface|record|enum)\s+\w*History\w*'
$agentDeclPattern = '^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*\b(?:class|interface|record|enum)\s+Agent\w*'

$opFiles = @(Get-CsFiles $opRoot)

# H1 History 类型声明
$h1 = 0
foreach ($f in $opFiles) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern $historyDeclPattern -AllMatches
    if ($hits) { $h1 += @($hits.Matches).Count }
}
if ($h1 -gt ([int]$baseline.HistoryTypeDeclarations.Value)) {
    Add-Violation 'H1' "Operation 内 *History* 类型声明 = $h1（基线 $([int]$baseline.HistoryTypeDeclarations.Value)）"
}

# H2 History 命名空间
$h2 = 0
foreach ($f in $opFiles) {
    if (Select-String -LiteralPath $f.FullName -Pattern '^\s*namespace\s+[\w.]*\bHistory\b' -Quiet) { $h2++ }
}
if ($h2 -gt ([int]$baseline.HistoryNamespaceFiles.Value)) {
    Add-Violation 'H2' "Operation 内 History 命名空间文件 = $h2（基线 $([int]$baseline.HistoryNamespaceFiles.Value)）"
}

# H3 层向：Frame 不得 using Agent.*
$h3 = 0
foreach ($f in (Get-CsFiles (Join-Path $frameRoot 'CsharpPrj'))) {
    if (Select-String -LiteralPath $f.FullName -Pattern '^\s*using\s+Agent\.' -Quiet) { $h3++ }
}
if ($h3 -gt ([int]$baseline.FrameUsingAgent.Value)) {
    Add-Violation 'H3' "Frame 内 using Agent.* 文件 = $h3（基线 $([int]$baseline.FrameUsingAgent.Value)）"
}

# H4 Operation 内 Agent* 类型声明
$h4 = 0
foreach ($f in $opFiles) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern $agentDeclPattern -AllMatches
    if ($hits) { $h4 += @($hits.Matches).Count }
}
if ($h4 -gt ([int]$baseline.FrameAgentTypeDeclarations.Value)) {
    Add-Violation 'H4' "Operation 内 Agent* 类型声明 = $h4（基线 $([int]$baseline.FrameAgentTypeDeclarations.Value)）"
}

# H5 提及 History 的文件数（棘轮，只报不拦）
$h5 = @($opFiles | Select-String -Pattern 'History' -List).Count
if ($h5 -gt ([int]$baseline.OperationHistoryMentionFiles.Value)) {
    Add-Violation 'H5' "Operation 提及 History 的文件 = $h5 > 基线 $([int]$baseline.OperationHistoryMentionFiles.Value)（棘轮只降不升）"
}

# NC 正/负对照
if ($NegativeControl) {
    $pos = [regex]::IsMatch('public sealed class HistoryRecord', $historyDeclPattern)
    $neg = [regex]::IsMatch('// 历史容器（History）注释', $historyDeclPattern)
    $agentPos = [regex]::IsMatch('public sealed record AgentLoopState(', $agentDeclPattern)
    if (-not $pos) { Add-Violation 'NC' '正对照未命中：合成样本 class HistoryRecord 未被 H1 正则捕获' }
    if ($neg) { Add-Violation 'NC' '负对照误命中：注释文本被判为类型声明' }
    if (-not $agentPos) { Add-Violation 'NC' '正对照未命中：合成样本 record AgentLoopState 未被 H4 正则捕获' }
    Write-Host "[HistorySemantics] NC pos=$pos neg=$neg agentPos=$agentPos"
}

$hard = @($violations | Where-Object { $_ -match '^(H1|H2|H3|H4|NC) ' })
$soft = @($violations | Where-Object { $_ -match '^H5 ' })

Write-Host "  H1 Operation *History* 类型声明 = $h1"
Write-Host "  H2 Operation History 命名空间文件 = $h2"
Write-Host "  H3 Frame using Agent.* 文件 = $h3"
Write-Host "  H4 Operation Agent* 类型声明 = $h4"
Write-Host "  H5 Operation History 提及文件 = $h5（基线 $([int]$baseline.OperationHistoryMentionFiles.Value)）"

if ($soft.Count -gt 0) {
    Write-Host "[HistorySemantics] 棘轮提示 $($soft.Count) 项：" -ForegroundColor Yellow
    foreach ($w in $soft) { Write-Host "  - $w" -ForegroundColor Yellow }
}
if ($hard.Count -eq 0) {
    Write-Host '[HistorySemantics] 硬判据全过（H1/H2/H3/H4/NC）'
    exit 0
}
Write-Host "[HistorySemantics] 违规 $($hard.Count) 项：" -ForegroundColor Red
foreach ($v in $hard) { Write-Host "  - $v" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
