#requires -Version 5.1
<#
.SYNOPSIS
    工作流 ↔ 节点域对应守卫（NodeFlowDomainMatch）—— `[Node] uses=/produces=` 域须在 Config\nodes\*.ushar 的 [Domain] 集内。
.DESCRIPTION
    U4（统一节点管理框架）：工作流步骤声明其消费/产出的节点域（`uses=` 读消费域 / `produces=` 写产出域）。
    判据：出现于 `[Node]` 行的域 token 必须 ∈ [Domain] 登记集（双向对账；未声明域 ⇒ 违规）。
    载体 = 结构事实（域 token ∈ 声明集）；不引执行者语义。
    模式：默认报告；-Check 违规 → 退出码 1。

.PARAMETER Root
    仓库根目录（缺省由脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。
.PARAMETER Check
    校验模式：存在违规 → 退出码 1。
.PARAMETER ShowDetail
    输出逐项实测计数。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NodeGateCommon.ps1')
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$nodesDir     = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
$workflowsDir = Join-Path $Root 'Projects\Plug\Agent\Config\workflows'
# U4 域对应声明面（[Node] uses=/produces=）
$domainMatchFiles = @('history-segment.ushar', 'history-compose.ushar', 'history-append.ushar', 'conversation-send-region.ushar')

# 基线单源 = Baselines\NodeFlowDomainMatch.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$b = Import-GateBaseline -Name NodeFlowDomainMatch
$domainTokenFloor = [int]$b.DomainTokenFloor.Value

$violations = [System.Collections.Generic.List[string]]::new()

Write-GateHeader '工作流↔节点域对应守卫（NodeFlowDomainMatch：uses=/produces= 域在册）'

# [Domain] 登记集（唯一权威 = Config/nodes/*.ushar）
$domains = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($f in @(Get-ChildItem -LiteralPath $nodesDir -Filter '*.ushar' -File -ErrorAction SilentlyContinue)) {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $f.FullName -Raw), '(?m)^\s*\[Domain\]\s+(?<n>[A-Za-z0-9_\-]+)')) {
        [void]$domains.Add($m.Groups['n'].Value)
    }
}
if ($domains.Count -eq 0) {
    $violations.Add("扫描面失效：[Domain] 登记集为空（$nodesDir）")
}

<# 抽取 [Node] 行内的 uses=/produces= 域 token（跳注释行）。 #>
function Get-FlowDomainTokens {
    param([string]$Path)
    $tokens = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Path)) { return $tokens }
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $t = $line.TrimStart()
        if ($t.StartsWith('#') -or $t.StartsWith('//')) { continue }
        if ($t -notmatch '^\[Node\]') { continue }
        foreach ($field in @('uses', 'produces')) {
            if ($line -match "(?<![A-Za-z0-9_\-])$field\s*=\s*(?<v>[A-Za-z0-9_,\-]+)") {
                foreach ($tok in ($Matches['v'] -split ',')) {
                    $tok = $tok.Trim()
                    if ($tok) { $tokens.Add([PSCustomObject]@{ Field = $field; Token = $tok }) }
                }
            }
        }
    }
    return $tokens
}

$totalTokens = 0
$checkedFiles = 0
foreach ($file in $domainMatchFiles) {
    $path = Join-Path $workflowsDir $file
    if (-not (Test-Path -LiteralPath $path)) {
        $violations.Add("U4 声明面缺失：$file")
        continue
    }
    $checkedFiles++
    foreach ($entry in (Get-FlowDomainTokens -Path $path)) {
        $totalTokens++
        if (-not $domains.Contains($entry.Token)) {
            $violations.Add("U4 未声明域：$file → $($entry.Field)=$($entry.Token)（未在 Config/nodes/*.ushar 的 [Domain] 集内）")
        }
    }
}

# 正对照：域 token 总数 ≥ 基线（声明被删 / 扫描面失效 ⇒ 恒绿）
if ($totalTokens -lt $domainTokenFloor) {
    $violations.Add("U4 正对照失效：域 token 总数 $totalTokens < 基线 $domainTokenFloor（声明被删或扫描面失效？）")
}

# 负对照：合成未声明域必须被同一判据命中
$probe = Join-Path ([System.IO.Path]::GetTempPath()) ("nfdm-probe-" + [guid]::NewGuid().ToString('n') + ".ushar")
try {
    Set-Content -LiteralPath $probe -Value "[Node] probe: fn=probe | uses=__no_such_domain__" -Encoding UTF8
    $hit = $false
    foreach ($entry in (Get-FlowDomainTokens -Path $probe)) {
        if (-not $domains.Contains($entry.Token)) { $hit = $true }
    }
    if (-not $hit) { $violations.Add("U4 负对照失效：合成未声明域未被判据命中（判据恒真？）") }
} finally {
    Remove-Item -LiteralPath $probe -ErrorAction SilentlyContinue
}

if ($ShowDetail) {
    Write-Host ("  实测：[Domain] 登记 {0} · 对账文件 {1}/{2} · 域 token {3}（下限 {4}）" -f `
        $domains.Count, $checkedFiles, $domainMatchFiles.Count, $totalTokens, $domainTokenFloor)
}

Write-GateVerdict -Violations $violations -Check:$Check -Key 'NodeFlowDomainMatch'
