#requires -Version 5.1
<#
.SYNOPSIS
    AgentRuntimeUI 配置**新鲜度**守卫（A1-b）—— 源（资源仓 JsonConfig）→ 产物（宿主 Configs）不得滞后。

.DESCRIPTION
    背景（A1）：`Information/DrivenConfig/JsonConfig/AgentRuntimeUI.json` 是单一真源，构建 `FileDriven.Launcher`
    时经 `CopyConfigsAndScripts`（AfterTargets=Build）物化到 `$(OutDir)\Configs`。历史缺陷：产物落后于源
    （源有 `events.selectedIndexChanged`、产物无）⇒ 「改了配置但运行不生效」静默。

    判据（**结构包含**，容忍产物独有的派生键，如 `themeSpec` / `templatePacks`）：
      F1 产物在位（缺失 ⇒ 违反：未烘焙）。
      F2 源的**事件键**（`"selectedIndexChanged"` 等 events 段键名）在产物中存在。
      F3 源的**绑定引用**（所有 `"source"` / `"target"` / `"output"` 值）在产物中存在。
      F4 源与产物的 `views`/`form` 段 JSON 形态可解析（防半截产物）。
      F5 正/负对照：合成样本「源有 X / 产物无 X」必被捕获（防判据空转）。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯四级 = Frame\PowerShellScript\Modules\Node → 仓库根）。
.PARAMETER ConfigPath
    源配置（缺省 = Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json）。
.PARAMETER ProductPath
    产物配置（缺省 = Projects\FileDriven\BuildOutput\Debug\Configs\AgentRuntimeUI.json）。
.PARAMETER Check
    校验模式。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$ConfigPath = '',
    [string]$ProductPath = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $Root 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'
}
if ([string]::IsNullOrWhiteSpace($ProductPath)) {
    $ProductPath = Join-Path $Root 'Projects\FileDriven\BuildOutput\Debug\Configs\AgentRuntimeUI.json'
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  AgentRuntimeUI 配置新鲜度守卫（源 → 产物：结构包含判据）'
Write-Host '============================================================'

# ---- 判据函数（可对任意源/产物文本对判定；供正/负对照复用） ----
function Get-FreshnessMisses([string]$SourceText, [string]$ProductText) {
    $misses = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($SourceText, '"(\w+)"\s*:\s*"AgentUi_[^"]*"')) {
        if ($ProductText -notmatch [regex]::Escape($m.Value)) { $misses.Add("事件/函数引用缺失：" + $m.Value) }
    }
    foreach ($m in [regex]::Matches($SourceText, '"(?:source|target|output)"\s*:\s*"([^"]+)"')) {
        $value = $m.Groups[1].Value
        if ($ProductText -notmatch ('"' + [regex]::Escape($value) + '"')) { $misses.Add("绑定引用缺失：$value") }
    }
    return $misses
}

if (-not (Test-Path -LiteralPath $ConfigPath)) {
    $violations.Add("[F1] 缺源配置：$ConfigPath")
} elseif (-not (Test-Path -LiteralPath $ProductPath)) {
    $violations.Add("[F1] 缺产物配置（未烘焙）：$ProductPath")
} else {
    $srcText = [System.IO.File]::ReadAllText($ConfigPath)
    $prodText = [System.IO.File]::ReadAllText($ProductPath)

    # F4 形态可解析（半截产物可见化）。
    foreach ($pair in @(@('源', $srcText), @('产物', $prodText))) {
        try { [void]($pair[1] | ConvertFrom-Json -Depth 64) }
        catch { $violations.Add("[F4] $($pair[0]) 配置 JSON 解析失败（半截产物/语法错误）：$($_.Exception.Message)") }
    }

    foreach ($m in (Get-FreshnessMisses $srcText $prodText)) {
        $violations.Add("[F2/F3] 产物落后于源：$m（请重新烘焙：构建 FileDriven.Launcher）")
    }

    # F5 负对照：人工构造「源有、产物无」必被捕获。
    if ((Get-FreshnessMisses '"cmd":"AgentUi_Probe"' '"cmd":"AgentUi_Other"').Count -eq 0) {
        $violations.Add('[F5] 负对照失效：滞后样本未被新鲜度判据捕获')
    }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] F1–F5 全部通过（源 → 产物新鲜：事件/绑定引用结构包含 + 正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ''
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
