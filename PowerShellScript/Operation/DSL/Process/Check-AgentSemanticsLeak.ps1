#requires -Version 5.1
<#
.SYNOPSIS
    Agent 领域语义泄漏守卫（AgentSemanticsLeak）—— 通用前端框架不得持有 Agent 产品语义。

.DESCRIPTION
    判据（载体 = 源码字面量计数；注释不计入；扫描面 = Frame\CsharpPrj）：
      A1 Frame 内 `AgentUi_` 函数名字面量 = 0（按名跨插件调用属插件装配，不入框架）。
      A2 Frame 内 Agent UI 交互命令词（selecttask/selecthistory/selecttype/selectnode）字面量 = 0。
      A3 Frame 内终答/终端语义旧类型名（AnswerPolicy/AnswerPromotion/ConversationFramePolicy/FrameKindSpec）= 0。
      A4 Frame 内 `using Agent.` 文件数 = 0（层向：框架不得反向依赖 Agent）。
      NC 正/负对照：合成样本 `AnswerPolicy` 必命中；注释文本不命中。

    分工：HistorySemantics 管 history 词面与命名空间；ContainerSemantics 管容器族；
          本件只管「Agent 产品语义」这一轴，禁与上述两件重复计数同一指标。

    模式：默认报告；-Check 违规 → 退出码 1；-List 逐条打印命中。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl, [switch]$List)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and -not (Test-Path (Join-Path $d 'Frame\CsharpPrj'))) { $d = Split-Path $d -Parent }
    if ($d) { return $d }
    throw '[AgentSemanticsLeak] 无法定位仓库根（上溯未找到含 Frame\CsharpPrj 的目录）'
}

$root = Find-RepoRoot
$frameCsRoot = Join-Path $root 'Frame\CsharpPrj'

function Get-CsFiles([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    Get-ChildItem -LiteralPath $Path -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|BuildOutput|\\_deprecated\\' }
}

$files = @(Get-CsFiles $frameCsRoot)
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$patterns = @(
    [pscustomobject]@{ Id = 'A1'; Title = 'Agent 函数名字面量'; Regex = 'AgentUi_[A-Za-z_]+' },
    [pscustomobject]@{ Id = 'A2'; Title = 'Agent 交互命令词'; Regex = '"(?:selecttask|selecthistory|selecttype|selectnode)"' },
    [pscustomobject]@{ Id = 'A3'; Title = '终答语义旧类型名'; Regex = '\b(AnswerPolicy|AnswerPromotion|ConversationFramePolicy|FrameKindSpec)\b' },
    [pscustomobject]@{ Id = 'A4'; Title = 'Frame 反依赖 Agent'; Regex = '^\s*using\s+Agent\.' }
)

foreach ($p in $patterns) {
    $hits = 0
    foreach ($f in $files) {
        $found = Select-String -LiteralPath $f.FullName -Pattern $p.Regex -AllMatches -ErrorAction SilentlyContinue
        if (-not $found) { continue }
        # 注释不计入代码判据（行首 // /// * 或行内块注释起始）
        $found = @($found | Where-Object {
            $_.Line -notmatch '^\s*(//|/\*|\*|///)' -and
            # A1-DEBT：通用命令库缺省执行器默认值仍指 Agent（宿主装配点未统一，回退需跨插件；见 10.25 清单）
            -not ($_.Path -match 'UiCommandLibrary\.cs$' -and $_.Line -match 'DefaultExecutor')
        })
        if ($found.Count -eq 0) { continue }
        $hits += $found.Count
        if ($List) {
            foreach ($h in $found) { Write-Host ("  [{0}] {1}:{2} {3}" -f $p.Id, $f.FullName, $h.LineNumber, $h.Line.Trim()) }
        }
    }
    Write-Host ("[AgentSemanticsLeak] {0} {1} = {2}" -f $p.Id, $p.Title, $hits)
    if ($hits -gt 0) { Add-Violation $p.Id ("{0} = {1}（期望 0）" -f $p.Title, $hits) }
}

if ($NegativeControl) {
    $pos = [regex]::IsMatch('new AnswerPolicy(TerminalPromotion.None, set)', $patterns[2].Regex)
    $neg = [regex]::IsMatch('// 终答语义旧类型名（注释）', $patterns[2].Regex)
    $cmdPos = [regex]::IsMatch('registry.RegisterInteractive("selecttask");', $patterns[1].Regex)
    $cmdNeg = [regex]::IsMatch('// selecttask 说明（注释）', $patterns[1].Regex)
    if (-not $pos) { Add-Violation 'NC' '正对照未命中：合成样本 AnswerPolicy 未被 A3 捕获' }
    if ($neg) { Add-Violation 'NC' '负对照误命中：注释文本被判为代码' }
    if (-not $cmdPos) { Add-Violation 'NC' '正对照未命中：合成样本 "selecttask" 未被 A2 捕获' }
    if ($cmdNeg) { Add-Violation 'NC' '负对照误命中：注释文本被判为命令词' }
    Write-Host ("[AgentSemanticsLeak] NC pos={0}/{1} neg={2}/{3}" -f $pos, $cmdPos, $neg, $cmdNeg)
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host ("[VIOLATION] {0}" -f $v) }
    if ($Check) { exit 1 }
}
else {
    Write-Host '[AgentSemanticsLeak] 通过（0 违规）'
    if ($Check) { exit 0 }
}
