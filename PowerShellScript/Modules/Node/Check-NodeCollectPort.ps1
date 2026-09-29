#requires -Version 5.1
<#
.SYNOPSIS
    节点收集式输入端口守卫（NodeCollectPort）—— `inports=nodes.*` 声明面的机器判据。

.DESCRIPTION
    沿《节点 → 任务 → 历史：组装归流 + 收集式输入端口（DSL 扩展）· 重构方案》§3.5 / §3.6：
    收集式输入端口是 `[Node] inports=` 的**唯一非具名端口**（保留 token），语义 = 本作用域全部节点输出源键整包入参；
    它不是键，故不可被当作字面键静默落空（R3 教训：未识别字段被静默忽略）。

    扫描面：
      Projects\Plug\Agent\Config\workflows\*.ushar        （编排声明面；_deprecated 排除）

    判据（C1–C3 硬判据；C4 正对照；C5 负对照）：
      C1 保留 token 白名单：`*` 形 token（`^\*$` 或含 `.*`）必须在**已启用**保留词表内
                            （未注册形 / 保留未启用形 ⇒ 违规；禁「看起来是通配、实为字面键永不命中」）
      C2 单次性：同一 `[Node] inports=` 出现 ≥2 个收集 token ⇒ 违规
      C3 出参唯一：含收集 token 而 `outports=` 键数 ≠ 1 ⇒ 违规（整包入参 + 多产物不可判定）
      C4 正对照（防假绿）：收集端口声明数 ≥ 基线，且其 `fn=` ∈ 投影类白名单
                            —— 当前为 0 时列 [NLP] 待办（声明面落地后自动收紧）
      C5 负对照（防假绿）：合成 `inports=node.*`（未注册形）必须被**同一判据函数**命中 C1

    判据载体 = 声明面结构事实（token 形态 / 计数 / 键数），不引入执行者叙述（SEP.JudgmentCarrierStructural）。
    运行时 counterpart = `Agent.Launcher\Loading\WorkflowFunctionRefValidator`（启动期 fail-fast，同一 C1–C3）。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（C1–C3 / C5）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-NodeCollectPort.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodeCollectPort.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$workflowsDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'

# ---- 保留 token 词表（已启用）----
# 权威定义 = Frame\CsharpPrj\Operation\HierarchyMethod\Nodes\Execution\NodeExecutionKeys.CollectNodePorts；
# `*`（数据空间全量）为保留**未启用**形（§3.2）⇒ 启用前不得通过 C1（启用须单独评审）。
$ReservedTokens = @('nodes.*')
# 收集端口的 fn= 白名单（投影类函数；§3.6 正对照 + §7-R3 缓解：LLM 步不得声明收集端口）
$ProjectionFunctions = @('context_project')
# 基线单源 = Baselines\NodeCollectPort.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name NodeCollectPort
# 正对照基线：声明面收集端口声明数 ≥ 基线（声明被删而门禁恒绿 = 失效）
$CollectDeclarationBaseline = [int]$g.CollectDeclaration.Value

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  节点收集式输入端口守卫（inports=nodes.*：整包入参声明面）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---------------- 解析（声明面结构事实；字段即语义） ----------------
# 行形态：`[Node] <name>: fn=<f> | inports=a,b | outports=c`
function Get-NodeDeclarations {
    param([string]$Path)

    $result = [System.Collections.Generic.List[object]]::new()
    $lineNo = 0
    foreach ($raw in (Get-Content -LiteralPath $Path)) {
        $lineNo++
        $line = $raw.Trim()
        if (-not $line.StartsWith('[Node]')) { continue }

        $body = $line.Substring('[Node]'.Length).Trim()
        $sep = $body.IndexOf(':')
        if ($sep -le 0) { continue }
        $name = $body.Substring(0, $sep).Trim()
        $rest = $body.Substring($sep + 1)

        $fn = ''
        $inports = @()
        $outports = @()
        foreach ($p in ($rest -split '\|')) {
            $seg = $p.Trim()
            $eq = $seg.IndexOf('=')
            if ($eq -le 0) { continue }
            $key = $seg.Substring(0, $eq).Trim()
            $val = $seg.Substring($eq + 1).Trim()
            switch ($key) {
                'fn'       { $fn = $val }
                'inports'  { $inports = @($val -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
                'outports' { $outports = @($val -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
            }
        }

        $result.Add([pscustomobject]@{
            Name     = $name
            Fn       = $fn
            Inports  = $inports
            Outports = $outports
            File     = (Split-Path -Leaf $Path)
            Line     = $lineNo
        })
    }
    return $result
}

# ---------------- 判据函数（C1–C3；正/负对照共用同一实现） ----------------
function Test-CollectPort {
    param(
        [object]$Decl,
        [string[]]$Reserved,
        [System.Collections.Generic.List[string]]$Sink
    )

    $collectCount = 0
    foreach ($port in $Decl.Inports) {
        if ($Reserved -contains $port) { $collectCount++; continue }
        if ($port -eq '*' -or $port.Contains('.*')) {
            $Sink.Add("[C1] $($Decl.File):$($Decl.Line) [$($Decl.Name)] 输入端口 '$port' 是未注册的 ``*`` 形 token（保留词表 = $($Reserved -join ',')）")
        }
    }
    if ($collectCount -gt 1) {
        $Sink.Add("[C2] $($Decl.File):$($Decl.Line) [$($Decl.Name)] 收集 token 出现 $collectCount 次（同一声明最多一次）")
    }
    if ($collectCount -gt 0 -and $Decl.Outports.Count -ne 1) {
        $Sink.Add("[C3] $($Decl.File):$($Decl.Line) [$($Decl.Name)] 含收集 token 但 outports 键数 = $($Decl.Outports.Count)（须恰 1 个）")
    }
    return $collectCount
}

if (-not (Test-Path -LiteralPath $workflowsDir)) {
    Write-Host "[FAIL] 未找到声明面目录：$workflowsDir" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

$files = @(Get-ChildItem -LiteralPath $workflowsDir -Filter '*.ushar' -File | Sort-Object Name)
$declarations = [System.Collections.Generic.List[object]]::new()
foreach ($f in $files) {
    foreach ($d in (Get-NodeDeclarations -Path $f.FullName)) { $declarations.Add($d) }
}

# ---------------- C1–C3 ----------------
$collectDecls = [System.Collections.Generic.List[object]]::new()
foreach ($decl in $declarations) {
    $n = Test-CollectPort -Decl $decl -Reserved $ReservedTokens -Sink $violations
    if ($n -gt 0) { $collectDecls.Add($decl) }
}

# ---------------- C4 正对照（防假绿；声明面未落地时列待办） ----------------
foreach ($decl in $collectDecls) {
    if ($ProjectionFunctions -notcontains $decl.Fn) {
        $violations.Add("[C4] $($decl.File):$($decl.Line) [$($decl.Name)] 收集端口出现在非投影类 fn='$($decl.Fn)'（白名单 = $($ProjectionFunctions -join ',')）")
    }
}
if ($collectDecls.Count -lt $CollectDeclarationBaseline) {
    $pending.Add("[C4] 正对照基线待建立：声明面收集端口声明数 $($collectDecls.Count) < 基线 $CollectDeclarationBaseline —— 待 Config\workflows\context-projection.ushar 落地（inports=nodes.* + fn=context_project）")
}

# ---------------- C5 负对照（同一判据函数对合成违规必须命中） ----------------
$probePath = Join-Path ([System.IO.Path]::GetTempPath()) ("ncp-probe-" + [guid]::NewGuid().ToString('n') + '.ushar')
try {
    Set-Content -LiteralPath $probePath -Value '[Node] bogus_projection: fn=context_project | inports=node.* | outports=context_messages' -Encoding utf8
    $probeSink = [System.Collections.Generic.List[string]]::new()
    foreach ($d in (Get-NodeDeclarations -Path $probePath)) {
        $null = Test-CollectPort -Decl $d -Reserved $ReservedTokens -Sink $probeSink
    }
    if (@($probeSink | Where-Object { $_ -like '*[C1]*' }).Count -eq 0) {
        $violations.Add('[C5] 负对照失效：合成 `inports=node.*` 未被 C1 判据命中（判据恒真/恒假？）')
    }
} finally {
    if (Test-Path -LiteralPath $probePath) { Remove-Item -LiteralPath $probePath -Force }
}

# ---------------- 汇总 ----------------
Write-Host ''
Write-Host ("  实测：声明文件 {0} / 节点声明 {1} / 收集端口声明 {2}" -f `
    $files.Count, $declarations.Count, $collectDecls.Count) -ForegroundColor Gray

if ($ShowDetail) {
    foreach ($d in $collectDecls) {
        Write-Host ("  收集端口声明 : {0}:{1} [{2}] fn={3} inports={4} outports={5}" -f `
            $d.File, $d.Line, $d.Name, $d.Fn, ($d.Inports -join ','), ($d.Outports -join ',')) -ForegroundColor Gray
    }
}

if ($ShowDetail -and $pending.Count -gt 0) {
    Write-Host '[NLP] --- 基线待建立（无新增即通过） ---' -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 节点收集式输入端口守卫：0 违规（C1–C3 声明面合规；C5 负对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 节点收集式输入端口守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host '  修复口径：保留 token 单一来源 = NodeExecutionKeys.CollectNodePorts；' -ForegroundColor Gray
Write-Host '            收集端口节点须 `outports=` 恰 1 键且 `fn=` ∈ 投影类白名单。' -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
