#requires -Version 5.1
<#
.SYNOPSIS
    工具面 / MCP 装配声明守卫（ToolFaceDeclaration）—— 装配声明化（问题②）的建面/对账判据。

.DESCRIPTION
    沿《统一节点管理框架-收敛设计》§3.2 / 《P0-P4 施工单》P0/P2：工具面 / MCP 装配的**唯一声明**
    = Config\tool-face.json（faces / tools / mcpServers 三段），装配器读声明建轨/建源；本门禁立
    「声明在册 + 执行端不外泄」的结构判据（报告模式起步）。

    判据（D1–D3 硬判据；D4 正对照；D5 负对照）：
      D1 声明在册：Config\tool-face.json 存在且 JSON 合法、三段（faces/tools/mcpServers）齐备
      D2 执行端不外泄：tools.<NotExposed>.expose 不得为 true（cline = 执行端 / answer = 内部面）
      D3 轨闭合：每个 face 的 tracks 非空（装配器按声明建轨，空轨 = 空面）
      D4 正对照（防假绿）：faces 数 ≥ 基线
      D5 负对照（防假绿）：合成「cline.expose=true」必须被**同一判据函数**命中 D2

    判据载体 = 声明面结构事实（键齐备 / 布尔值 / 计数），不引入执行者叙述（SEP.JudgmentCarrierStructural）。

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（D1–D5 违规 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Operation\DSL → 仓库根）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测（含 faces / tracks 明细）。

.EXAMPLE
    .\Check-ToolFaceDeclaration.ps1 -ShowDetail

.EXAMPLE
    .\Check-ToolFaceDeclaration.ps1 -Check
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

# 基线单源 = Baselines\ToolFaceDeclaration.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name ToolFaceDeclaration

$declPath = Join-Path $Root 'Projects\Plug\Agent\Config\tool-face.json'

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  工具面 / MCP 装配声明守卫（tool-face.json）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# ---------------- 判据函数（D2；正/负对照共用同一实现） ----------------
function Test-NotExposed {
    param(
        $ToolsNode,
        [string[]]$NotExposedIds,
        [System.Collections.Generic.List[string]]$Sink
    )

    if ($null -eq $ToolsNode) {
        $Sink.Add('[D1] tools 段缺失')
        return
    }
    foreach ($id in $NotExposedIds) {
        $entry = $ToolsNode.PSObject.Properties[$id]
        if ($null -ne $entry -and $entry.Value.expose -eq $true) {
            $Sink.Add("[D2] 工具 '$id' 被声明 expose=true（执行端 / 内部面禁外泄）")
        }
    }
}

# ---------------- D1 声明在册 ----------------
$decl = $null
if (-not (Test-Path -LiteralPath $declPath)) {
    $violations.Add("[D1] 未找到工具面声明 $declPath")
}
else {
    try {
        $decl = Get-Content -LiteralPath $declPath -Raw | ConvertFrom-Json
    }
    catch {
        $violations.Add("[D1] $declPath JSON 非法：$($_.Exception.Message)")
    }
    if ($null -ne $decl) {
        foreach ($seg in @('faces', 'tools', 'mcpServers')) {
            if ($null -eq $decl.PSObject.Properties[$seg]) {
                $violations.Add("[D1] 声明缺段：$seg")
            }
        }
    }
}

# ---------------- D2 执行端不外泄 + D3 轨闭合 ----------------
$faceCount = 0
if ($null -ne $decl) {
    $notExposed = @($g.NotExposed.Value)
    Test-NotExposed -ToolsNode $decl.tools -NotExposedIds $notExposed -Sink $violations

    if ($null -ne $decl.faces) {
        foreach ($faceProp in $decl.faces.PSObject.Properties) {
            $faceCount++
            $tracks = @($faceProp.Value.tracks)
            if ($tracks.Count -lt 1) {
                $violations.Add("[D3] face '$($faceProp.Name)' 的 tracks 为空（装配器无轨可建）")
            }
            if ($ShowDetail) {
                Write-Host ("  face {0} : tracks=[{1}]" -f $faceProp.Name, ($tracks -join ',')) -ForegroundColor DarkGray
            }
        }
    }
}

# ---------------- D4 正对照（防假绿） ----------------
$minFaces = [int]$g.FaceCount.Value
if ($faceCount -lt $minFaces) {
    $violations.Add("[D4] 正对照基线：声明 faces 数 $faceCount < 基线 $minFaces（声明被删而门禁恒绿 = 失效）")
}

# ---------------- D5 负对照（同一判据函数对合成样本必须命中） ----------------
$probeTools = ConvertFrom-Json '{ "cline": { "expose": true }, "answer": { "expose": true } }'
$probeSink = [System.Collections.Generic.List[string]]::new()
Test-NotExposed -ToolsNode $probeTools -NotExposedIds @($g.NotExposed.Value) -Sink $probeSink
if (@($probeSink | Where-Object { $_ -like '*[D2]*' }).Count -eq 0) {
    $violations.Add('[D5] 负对照失效：合成 cline.expose=true 未被 D2 判据命中（判据恒真/恒假？）')
}

# ---------------- 汇总 ----------------
Write-Host ("  实测：声明 faces {0} 个 / 不外泄工具 {1} 个" -f $faceCount, (@($g.NotExposed.Value).Count)) -ForegroundColor Gray

if ($ShowDetail -and $pending.Count -gt 0) {
    Write-Host '[NLP] --- 待办 ---' -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] 工具面装配声明守卫：0 违规（D1/D3 声明在册；D2 不外泄；D4/D5 对照命中）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 工具面装配声明守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host '  修复口径：Config\tool-face.json 三段齐备；cline/answer 恒 expose=false；face.tracks 非空。' -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0
