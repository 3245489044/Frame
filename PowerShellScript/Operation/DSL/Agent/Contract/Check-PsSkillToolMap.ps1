#requires -Version 5.1
<#
.SYNOPSIS
    SKILL 表 ↔ PsRuntimeCallableTrack.ActionToTool 对账（R-05）。

.DESCRIPTION
    单一映射 = PsRuntimeCallableTrack.ActionToTool；skills/mcp/ps-psm1/SKILL.md 的工具表是提示词面副本。
    断言：
      M1 ActionToTool.Keys ⊆ 契约文件 action 集（契约根 = AgentRoot 下 *.contracts.json 全量扫描）；
      M2 ActionToTool.Values == SKILL.md 契约轨工具名集（含 PsRuntimeCallableTrack 的表格行反引号 token）；
      M3 正/负对照：集合比较器对合成样本须给出预期差集（防判据恒真/恒假）。
    任一侧增删工具而未同步 ⇒ 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$AgentRoot = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$ledgerForge = Get-GateLedgerForge -RepoRoot $repoRoot
if (-not $ledgerForge) {
    Write-Host '[FAIL] 无法执行：LedgerForge 未构建（键集比较不可用）' -ForegroundColor Red
    exit 2
}
if ([string]::IsNullOrWhiteSpace($AgentRoot)) {
    $AgentRoot = Join-Path $repoRoot 'Projects\Plug\Agent'
}

$violations = [System.Collections.Generic.List[string]]::new()
$warnings   = [System.Collections.Generic.List[string]]::new()

# 键集比较统一到 C# LedgerKeyClosure(Ordinal)（A8）；本门只保留 M1/M2 业务判据。
function Compare-Set {
    param([string[]]$Left, [string[]]$Right)
    return (Compare-GateKeySet -Reference $Left -Other $Right -ReferenceLabel 'left' -OtherLabel 'right' -RepoRoot $repoRoot)
}

# ---- M1/M2 输入：映射表 ----
$trackFile = Join-Path $AgentRoot 'src\Agent.McpRegistryHost\Tracks\PsRuntimeCallableTrack.cs'
if (-not (Test-Path -LiteralPath $trackFile)) {
    $violations.Add("缺映射单源 PsRuntimeCallableTrack.cs：$trackFile")
}
$mapActions = @()
$mapTools = @()
if (Test-Path -LiteralPath $trackFile) {
    $trackText = Get-Content -LiteralPath $trackFile -Raw -Encoding UTF8
    # 只解析 ActionToTool 字典块（避免误抓同文件其它索引器/类型字面量）。
    $block = [regex]::Match($trackText, '(?s)ActionToTool\s*=\s*new\s+Dictionary<string,\s*string>[^\{]*\{(?<body>.*?)\};')
    if (-not $block.Success) {
        $violations.Add('ActionToTool 字典块不可解析（映射表结构变更？）')
    }
    else {
        foreach ($m in [regex]::Matches($block.Groups['body'].Value, '\[\s*"(?<action>[^"]+)"\s*\]\s*=\s*"(?<tool>[^"]+)"')) {
            $mapActions += $m.Groups['action'].Value
            $mapTools += $m.Groups['tool'].Value
        }
    }
    if ($mapActions.Count -eq 0) { $violations.Add('ActionToTool 解析为空（映射表结构变更？）') }
}

# ---- M1 输入：契约 action 集（AgentRoot 下全量 *.contracts.json）----
$contractActions = @()
# 契约根单源（A4）：唯一解析实现 = C# PowerShellHostRoots（经 LedgerForge ps-runtime-facts 导出）。
# PS 不再自算 repoRoot/projectRoot/contractDirs，也不再自带 Frame 回退分支。
$frameRoot = Join-Path $GateRuntimeRoot 'Frame'
$factsOut = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('ps-runtime-facts-' + [guid]::NewGuid().ToString('N') + '.json'))
try {
    & $ledgerForge ps-runtime-facts --repo $repoRoot --agent $AgentRoot --frame-root $frameRoot --out $factsOut | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $factsOut)) {
        Write-Host '[FAIL] 无法执行：ps-runtime-facts 未产出（契约根解析不可用）' -ForegroundColor Red
        exit 2
    }
    $runtimeFacts = Get-Content -LiteralPath $factsOut -Raw -Encoding UTF8 | ConvertFrom-Json
}
finally {
    if (Test-Path -LiteralPath $factsOut) { Remove-Item -LiteralPath $factsOut -Force }
}
# 口径对齐断言：运行期不得另有一份契约根解析（必须复用同一实现）。
$runtimeHostSource = Join-Path $AgentRoot 'src\Agent.Modules\Framework\Invoke\PsRuntime\PsRuntimeHost.cs'
if (-not (Test-Path -LiteralPath $runtimeHostSource)) {
    $violations.Add("缺 PsRuntimeHost.cs：$runtimeHostSource")
}
elseif ((Get-Content -LiteralPath $runtimeHostSource -Raw -Encoding UTF8) -notmatch 'PowerShellHostRoots\.Resolve') {
    $violations.Add('[A4] PsRuntimeHost 未复用 PowerShellHostRoots（契约根口径双实现）')
}
$contractRoots = @($runtimeFacts.resolvedRoots)
foreach ($missing in @($runtimeFacts.missingRoots)) { $violations.Add("契约根不存在：$missing") }
if ($contractRoots.Count -eq 0) {
    $violations.Add("契约根解析为空（projectRoot/contractDirs 无效）：base=$($runtimeFacts.baseRoot)")
}
foreach ($root in $contractRoots) {
    foreach ($file in (Get-ChildItem -LiteralPath $root -Recurse -Filter '*.contracts.json' -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch (Get-GateSourceExcludePattern) })) {
        try {
            $doc = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            foreach ($c in @($doc.contracts)) {
                if ($null -ne $c -and $c.identity -and $c.identity.action) { $contractActions += [string]$c.identity.action }
            }
        }
        catch {
            $violations.Add("契约文件不可解析：$($file.FullName) — $($_.Exception.Message)")
        }
    }
}
if ($contractActions.Count -eq 0) { $violations.Add('契约 action 集为空（契约根缺失或格式变更）') }

$actionDiff = Compare-Set -Left $mapActions -Right $contractActions
foreach ($a in $actionDiff.OnlyLeft) { $violations.Add("[M1] ActionToTool 的 action '$a' 不在契约文件中") }
foreach ($a in $actionDiff.OnlyRight) { $warnings.Add("[M1] 契约 action '$a' 未映射工具（非本轨 action 可忽略）") }

# ---- M2 输入：SKILL.md 契约轨工具名集 ----
$skillFile = Join-Path $AgentRoot 'skills\mcp\ps-psm1\SKILL.md'
if (-not (Test-Path -LiteralPath $skillFile)) {
    $violations.Add("缺 SKILL.md：$skillFile")
}
$skillTools = @()
if (Test-Path -LiteralPath $skillFile) {
    $trackLine = @(Get-Content -LiteralPath $skillFile -Encoding UTF8 | Where-Object { $_ -match 'PsRuntimeCallableTrack' })
    if ($trackLine.Count -eq 0) {
        $violations.Add('[M2] SKILL.md 无契约轨工具行（含 PsRuntimeCallableTrack 的表格行缺失）')
    }
    else {
        foreach ($line in $trackLine) {
            foreach ($m in [regex]::Matches($line, '`(?<tool>[a-z][a-z0-9_]*)`')) {
                $tool = $m.Groups['tool'].Value
                if ($tool -ne 'cc_call_tool') { $skillTools += $tool }
            }
        }
    }
}

$toolDiff = Compare-Set -Left $mapTools -Right $skillTools
foreach ($t in $toolDiff.OnlyLeft) { $violations.Add("[M2] 映射工具 '$t' 未出现在 SKILL.md 契约轨工具行") }
foreach ($t in $toolDiff.OnlyRight) { $violations.Add("[M2] SKILL.md 工具 '$t' 不在 ActionToTool（提示词面与代码面不一致）") }

# ---- M3 正/负对照：比较器自证 ----
$probe = Compare-Set -Left @('a', 'b') -Right @('b', 'c')
if (($probe.OnlyLeft -join ',') -ne 'a' -or ($probe.OnlyRight -join ',') -ne 'c') {
    $violations.Add('[M3] 负对照失效：集合比较器未给出预期差集')
}

foreach ($w in $warnings) { Write-Host "WARN: $w" -ForegroundColor Yellow }
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    exit 1
}
Write-Host ("PsSkillToolMap gate OK（actions={0}, tools={1}）" -f $mapActions.Count, $mapTools.Count) -ForegroundColor Green
exit 0
