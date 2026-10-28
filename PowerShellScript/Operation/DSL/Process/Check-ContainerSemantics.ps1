#requires -Version 5.1
<#
.SYNOPSIS
    容器族语义守卫（ContainerSemantics）—— Frame 通用层去领域名 + 层向 + 底座不下降 + 棘轮。

.DESCRIPTION
    判据（C1–C7，载体 = 源码符号级静态计数，注释不计）：
      C1 通用层领域名残留：Operation 内 `(class|interface|record|enum) *History*` 声明数 ≤ 基线（0）。
      C2 层向：Frame 内 `using Agent.` = 0（Operation 不得反向依赖 Agent）。
      C3 命名空间：`Operation.HierarchyMethod.History` 命名空间留存文件数 ≤ 基线（0）。
      C4 载体表名字面量（ur_histories）≤ 基线（棘轮只降不升）。
      C5 通用底座不下降：`Share.TaskStatusPool` 引用文件数 ≥ 基线（Frame/Agent 两侧）。
      C6 Frame 内提及 History 的文件数 ≤ 基线（棘轮，债务项）。
      C7 `TaskRecord` 在 Frame 的引用文件数 ≤ 基线（棘轮，记录类型下迁债务）。
      NC 负对照：合成样本 `class HistoryRecord` 必命中 C1 正则；注释文本不命中。

    模式：默认报告；-Check 校验（C1/C2/C3/C5 违规 → 退出码 1；C4/C6/C7 只报不拦）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Frame\CsharpPrj'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[ContainerSemantics] 无法定位仓库根（上溯未找到含 Frame\CsharpPrj 的目录）'
}

$root = Find-RepoRoot
$frameRoot = Join-Path $root 'Frame'
$opRoot = Join-Path $frameRoot 'CsharpPrj\Operation'
$agentSrc = Join-Path $root 'Projects\Plug\Agent\src'
$baselinePath = Join-Path $frameRoot 'PowerShellScript\Baselines'
Import-Module (Join-Path $baselinePath 'GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name ContainerSemantics

$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }
function Get-Baseline([string]$Key) { return [int]$baseline[$Key].Value }

function Get-CsFiles([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    Get-ChildItem -LiteralPath $Path -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\' }
}

# C1 通用层领域名残留（符号级；注释不计）
$declPattern = '(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*\b(?:class|interface|record|enum)\s+\w*History\w*'
$opFiles = @(Get-CsFiles $opRoot)
$c1 = 0
foreach ($f in $opFiles) {
    $hits = Select-String -LiteralPath $f.FullName -Pattern $declPattern -AllMatches
    if ($hits) { $c1 += @($hits.Matches).Count }
}
if ($c1 -gt ([int]$baseline.OperationHistoryTypeDeclarations.Value)) {
    Add-Violation 'C1' "Operation 内 *History* 类型声明 = $c1（基线 $([int]$baseline.OperationHistoryTypeDeclarations.Value)）"
}

# C2 层向：Frame 不得 using Agent.*
$c2 = 0
foreach ($f in (Get-CsFiles (Join-Path $frameRoot 'CsharpPrj'))) {
    if (Select-String -LiteralPath $f.FullName -Pattern '^\s*using\s+Agent\.' -Quiet) { $c2++ }
}
if ($c2 -gt ([int]$baseline.FrameUsingAgent.Value)) {
    Add-Violation 'C2' "Frame 内 using Agent.* 文件 = $c2（基线 $([int]$baseline.FrameUsingAgent.Value)）"
}

# C3 命名空间留存
$c3 = 0
foreach ($f in $opFiles) {
    if (Select-String -LiteralPath $f.FullName -Pattern '^\s*namespace\s+Operation\.HierarchyMethod\.History' -Quiet) { $c3++ }
}
if ($c3 -gt ([int]$baseline.OperationHistoryNamespaceFiles.Value)) {
    Add-Violation 'C3' "Operation.HierarchyMethod.History 命名空间文件 = $c3（基线 $([int]$baseline.OperationHistoryNamespaceFiles.Value)）"
}

# C4 载体表名字面量（棘轮，只报不拦）
$c4 = @($opFiles | Select-String -Pattern 'ur_histories' -List).Count
if ($c4 -gt ([int]$baseline.CarrierTableLiterals.Value)) {
    Add-Violation 'C4' "Operation 内 ur_histories 字面量文件 = $c4（基线 $([int]$baseline.CarrierTableLiterals.Value)，棘轮只降不升）"
}

# C5 通用底座不下降
$c5Frame = @(Get-CsFiles (Join-Path $frameRoot 'CsharpPrj') | Select-String -Pattern 'Share\.TaskStatusPool' -List).Count
$c5Agent = @(Get-CsFiles $agentSrc | Select-String -Pattern 'Share\.TaskStatusPool' -List).Count
if ($c5Frame -lt ([int]$baseline.PoolFrameRefs.Value)) {
    Add-Violation 'C5' "Share.TaskStatusPool Frame 引用文件 = $c5Frame < 基线 $([int]$baseline.PoolFrameRefs.Value)（底座被误迁）"
}
if ($c5Agent -lt ([int]$baseline.PoolAgentRefs.Value)) {
    Add-Violation 'C5' "Share.TaskStatusPool Agent 引用文件 = $c5Agent < 基线 $([int]$baseline.PoolAgentRefs.Value)（底座被误迁）"
}

# C6 / C7 棘轮（只报）
$c6 = @(Get-CsFiles (Join-Path $frameRoot 'CsharpPrj') | Select-String -Pattern 'History' -List).Count
if ($c6 -gt ([int]$baseline.FrameHistoryMentionFiles.Value)) {
    Add-Violation 'C6' "Frame 提及 History 的文件 = $c6 > 基线 $([int]$baseline.FrameHistoryMentionFiles.Value)（棘轮只降不升）"
}
$c7 = @(Get-CsFiles (Join-Path $frameRoot 'CsharpPrj') | Select-String -Pattern '\bTaskRecord\b' -List).Count
if ($c7 -gt ([int]$baseline.TaskRecordFrameRefs.Value)) {
    Add-Violation 'C7' "Frame 引用 TaskRecord 的文件 = $c7 > 基线 $([int]$baseline.TaskRecordFrameRefs.Value)（棘轮只降不升）"
}

# C8 Agent 侧提及 History 的文件数（报告；基线仅记录，不拦）
$c8 = @(Get-CsFiles $agentSrc | Select-String -Pattern 'History' -List).Count

# NC 负对照
if ($NegativeControl) {
    $pos = [regex]::IsMatch('public sealed class HistoryRecord', $declPattern)
    $neg = [regex]::IsMatch('// 历史容器（History）注释', $declPattern)
    if (-not $pos) { Add-Violation 'NC' '正对照未命中：合成样本 class HistoryRecord 未被 C1 正则捕获' }
    if ($neg) { Add-Violation 'NC' '负对照误命中：注释文本被判为类型声明' }
    Write-Host "[ContainerSemantics] NC pos=$pos neg=$neg"
}

$hard = @($violations | Where-Object { $_ -match '^(C1|C2|C3|C5|NC) ' })
$soft = @($violations | Where-Object { $_ -match '^(C4|C6|C7) ' })

Write-Host "  C1 Operation*History*类型声明 = $c1"
Write-Host "  C2 Frame using Agent.*        = $c2"
Write-Host "  C3 History 命名空间文件       = $c3"
Write-Host "  C4 ur_histories 文件          = $c4"
Write-Host "  C5 底座引用                   = Frame $c5Frame / Agent $c5Agent"
Write-Host "  C6 Frame History 提及文件     = $c6"
Write-Host "  C7 Frame TaskRecord 引用文件  = $c7"
Write-Host "  C8 Agent History 提及文件     = $c8（记录基线 $([int]$baseline.AgentHistoryMentionFiles.Value)）"

if ($soft.Count -gt 0) {
    Write-Host "[ContainerSemantics] 棘轮提示 $($soft.Count) 项：" -ForegroundColor Yellow
    foreach ($w in $soft) { Write-Host "  - $w" -ForegroundColor Yellow }
}
if ($hard.Count -eq 0) {
    Write-Host '[ContainerSemantics] 硬判据全过（C1/C2/C3/C5/NC）'
    exit 0
}
Write-Host "[ContainerSemantics] 违规 $($hard.Count) 项：" -ForegroundColor Red
foreach ($v in $hard) { Write-Host "  - $v" -ForegroundColor Red }
if ($Check) { exit 1 }
exit 0
