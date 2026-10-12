#requires -Version 5.1
<#
.SYNOPSIS
    spec 端口台账漂移守卫（SpecPortLedger）—— R3「声明面 ↔ 端口登记」对账的机器判据。

.DESCRIPTION
    背景（《Agent 现存红清单与修复建议-修复进展与遗留》§2 R3 / §1 簇 A）：
    用例 `given ports` 声明式替身装配此前**不校验引用键是否已登记**，绑定未登记键只会在
    运行期抛 PortNotRegisteredException（红长期不可见）。本件把该对账前移为**门禁期**判据。

    判据（载体 = 结构事实：文本块 / 键集合；禁执行者自述）：
      R1 登记面向闭：任一用例 `given ports` 引用键 ∈ 端口台账（`Standards\ports\*.specs` 的 `port <键>`）。
      R2 产物字段面：内联 `chain` 端口块的 `impl.productFields`（若显式声明）须覆盖同块 `out.fields`
         （超集语义；缺项即漂移）。
      U  正 / 负对照：判据函数对合成样本行为正确（防判据被改坏 ⇒ 空转）。

    模式：默认报告（不设退出码）；-Check 时违规 → 退出码 1。只读仓库，零侧写。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER ShowDetail
    输出逐条实测落点。
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

$violations = [System.Collections.Generic.List[string]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

# 从 Start 行起，定位首个 '{' 起的花括号平衡块（引号感知）；返回 Text 与结束行号。
function Get-BraceBlock {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines,
        [Parameter(Mandatory)][int]$Start
    )

    $sb = [System.Text.StringBuilder]::new()
    for ($i = $Start; $i -lt $Lines.Count; $i++) { [void]$sb.Append($Lines[$i]); [void]$sb.Append("`n") }
    $text = $sb.ToString()

    $open = $text.IndexOf('{')
    if ($open -lt 0) { return [pscustomobject]@{ Text = ''; EndLine = $Start } }

    $depth = 0; $inString = $false; $escaped = $false
    for ($j = $open; $j -lt $text.Length; $j++) {
        $c = $text[$j]
        if ($inString) {
            if ($escaped) { $escaped = $false }
            elseif ($c -eq '\') { $escaped = $true }
            elseif ($c -eq '"') { $inString = $false }
            continue
        }
        if ($c -eq '"') { $inString = $true; continue }
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') {
            $depth--
            if ($depth -eq 0) {
                $body = $text.Substring($open, $j - $open + 1)
                $endLine = $Start + (([regex]::Matches($text.Substring(0, $j), "`n")).Count)
                return [pscustomobject]@{ Text = $body; EndLine = $endLine }
            }
        }
    }
    return [pscustomobject]@{ Text = ''; EndLine = $Start }
}

# 端口键形态：<类型>.<成员>（`given ports` 块内的裸键形态）。
function Get-PortKeys {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $keys = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($Text, '(?m)^\s*(?<k>[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*)\s*:')) {
        $keys.Add($m.Groups['k'].Value)
    }
    return $keys.ToArray()
}


# ---- 端口台账键集（`port <键>`）----
$portsDir = Join-Path $Root 'Environment\CentralTest\AgentTest\Agent.TestShared\Standards\ports'
$ledgerKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
if (Test-Path -LiteralPath $portsDir) {
    foreach ($f in @(Get-ChildItem -LiteralPath $portsDir -Filter '*.specs' -File -ErrorAction SilentlyContinue)) {
        foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
            $m = [regex]::Match($line, '^\s*port\s+(?<k>\S+)\s*$')
            if ($m.Success) { [void]$ledgerKeys.Add($m.Groups['k'].Value) }
        }
    }
}
if ($ledgerKeys.Count -lt 1) { $violations.Add('[U] 端口台账为空（判据无标的假绿）：' + $portsDir) }

# ---- 采集用例 given ports 引用键 + 内联 chain 产物字段面 ----
$caseRoot = Join-Path $Root 'Environment\CentralTest\AgentTest'
$givenHit = 0
$chainHit = 0
if (Test-Path -LiteralPath $caseRoot) {
    $caseFiles = @(Get-ChildItem -LiteralPath $caseRoot -Recurse -Filter '*.specs' -File -ErrorAction SilentlyContinue |
                   Where-Object { $_.FullName -notmatch '\\Standards\\ports\\' })
    foreach ($f in $caseFiles) {
        $lines = [IO.File]::ReadAllLines($f.FullName)
        $rel = $f.FullName.Substring($Root.Length).TrimStart('\')

        for ($i = 0; $i -lt $lines.Count; $i++) {
            # R1：given ports 块
            if ($lines[$i] -match '^\s*given\s+ports\b') {
                $blk = Get-BraceBlock -Lines $lines -Start $i
                foreach ($k in (Get-PortKeys -Text $blk.Text)) {
                    $givenHit++
                    if (-not $ledgerKeys.Contains($k)) {
                        $violations.Add("[R1] given ports 键未在端口台账登记：$k （$rel）")
                    }
                }
                if ($blk.EndLine -gt $i) { $i = $blk.EndLine }
                continue
            }

            # R2：内联 chain 端口块 productFields ⊇ out.fields
            if ($lines[$i] -match '^\s*chain\s+\S+\s*$') {
                $blk = Get-BraceBlock -Lines $lines -Start $i
                if ($blk.Text) {
                    $obj = $null
                    try { $obj = $blk.Text | ConvertFrom-Json } catch { $obj = $null }
                    if ($obj -and $obj.PSObject.Properties['port']) {
                        foreach ($p in $obj.port.PSObject.Properties) {
                            $chainHit++
                            $impl = $p.Value.PSObject.Properties['impl']
                            $out = $p.Value.PSObject.Properties['out']
                            if (-not $impl -or -not $out) { continue }
                            $pf = $impl.Value.PSObject.Properties['productFields']
                            if (-not $pf) { continue }
                            $pfRaw = $pf.Value
                            $pfText = if ($pfRaw -is [System.Array]) { ($pfRaw -join '|') } else { [string]$pfRaw }
                            $oFields = $out.Value.PSObject.Properties['fields']
                            if (-not $oFields) { continue }
                            foreach ($fld in @($oFields.Value)) {
                                if ($pfText -notmatch ('(^|[\s,|])' + [regex]::Escape([string]$fld) + '($|[\s,|])')) {
                                    $violations.Add("[R2] impl.productFields 未覆盖 out.fields：$($p.Name).$fld （$rel）")
                                }
                            }
                        }
                    }
                }
                if ($blk.EndLine -gt $i) { $i = $blk.EndLine }
            }
        }
    }
}
$notes.Add("台账端口键 $($ledgerKeys.Count) · given ports 引用 $givenHit · 内联 chain 端口 $chainHit")

# ---- U 正 / 负对照（判据自证）----
$ctrlLines = @('x', '{', '  SkillCatalog.LoadProbe: { kind: fixed }', '}')
$ctrlBlock = Get-BraceBlock -Lines $ctrlLines -Start 0
$posOk = ($ctrlBlock.Text -ne '') -and ((Get-PortKeys -Text $ctrlBlock.Text) -contains 'SkillCatalog.LoadProbe')
$negOk = -not ((Get-PortKeys -Text "  notaport: x") -contains 'notaport')
$pfDelimPos = ('error|names|allCount' -match ('(^|[\s,|])' + [regex]::Escape('names') + '($|[\s,|])'))
$pfDelimNeg = -not ('error|names|allCount' -match ('(^|[\s,|])' + [regex]::Escape('nam') + '($|[\s,|])'))
if (-not $posOk) { $violations.Add('[U] 正对照失败：合成 given ports 块未被正确抽取（判据函数被改坏）') }
if (-not $negOk) { $violations.Add('[U] 负对照失败：非法键形态被误抽为端口键') }
if (-not $pfDelimPos) { $violations.Add('[U] 正对照失败：productFields 竖线分隔未被识别') }
if (-not $pfDelimNeg) { $violations.Add('[U] 负对照失败：productFields 前缀子串被误判为覆盖') }

# ---- 报告 ----
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  端口台账漂移守卫（SpecPortLedger）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
foreach ($n in $notes) { Write-Host ("  " + $n) -ForegroundColor Gray }

if ($ShowDetail -and $violations.Count -gt 0) {
    Write-Host '  --- 违规落点 ---' -ForegroundColor DarkGray
    foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] SpecPortLedger：0 违规（given ports ⊆ 台账 / productFields ⊇ out.fields / 正负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] SpecPortLedger 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0
