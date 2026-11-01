#requires -Version 7.0
<#
.SYNOPSIS
    帧身份守卫（FrameIdentity）—— 身份模板形状单值、声明↔编译视图双向绑定、ordinal 口径单值。

.DESCRIPTION
    背景：身份模板按域各写一份（`Config\nodes\*.ushar` 的 `identity`），靠人工同形；同一文件内
    `ordinal` 还有两种口径（类内序 vs 轮内全局到达序）⇒ 身份不可复算、漂移无红。

    判据（载体 = 声明原文 + 编译视图常量；注释不参与）：
      I1 形状单值：任何声明了 `{round}` 的 identity 必须恰为 `{container}.r{round}.{kind}.{ordinal}`
         （段序/分隔符改变 ⇒ 红：同形才可反解）。
      I2 双向绑定：`ProcessKeys.MultiRowIdentityTemplate` 的占位符集合与声明面同形模板一致（去 `:digits` 后逐字相等）。
      I3 口径单值：`ProcessKeys.cs` 声明文本内不得同时出现「类内到达序」与「轮内全局到达序」两种口径。
      I4 单键域：`{container}` 形态合法（Stream 单键），但不得夹带其它占位符。

    模式：默认报告；-Check 违规 → 退出码 1；-NegativeControl 自证判据可判红。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and $d -ne (Split-Path $d -Parent)) {
        if ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects'))) { return $d }
        $d = Split-Path $d -Parent
    }
    throw '[FrameIdentity] 无法定位仓库根（上溯未找到同时含 Frame\ 与 Projects\ 的目录）'
}

$root = Find-RepoRoot
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Id, [string]$Msg) { $script:violations.Add("$Id $Msg") }

$canonicalMultiRow = '{container}.r{round}.{kind}.{ordinal}'
$placeholdersAllowed = @('container', 'round', 'kind', 'ordinal', 'path', 'hash', 'storage', 'node')

function Get-DeclaredIdentities([string]$dir) {
    $result = @()
    if (-not (Test-Path -LiteralPath $dir)) { return $result }
    foreach ($file in (Get-ChildItem -LiteralPath $dir -Filter '*.ushar' -File)) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($m in [regex]::Matches($text, '(?m)^\s*identity\s+(\S+)')) {
            $result += [pscustomobject]@{ File = $file.Name; Template = $m.Groups[1].Value }
        }
    }
    return $result
}

function Test-IdentityShape([string]$template) {
    $normalized = $template -replace ':[a-zA-Z]+', ''
    if ($normalized -eq '{container}') { return $true }
    if ($normalized -notmatch '\{round') { return $true }
    return $normalized -ceq $canonicalMultiRow
}

function Get-PlaceholderSet([string]$template) {
    return @([regex]::Matches($template, '\{([a-zA-Z]+)(?::[a-zA-Z]+)?\}') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
}

function Test-OrdinalModeSingle([string]$text) {
    $inner = $text.Contains('类内到达序')
    $arrival = $text.Contains('轮内全局到达序')
    return -not ($inner -and $arrival)
}

# ---- I1 / I4 声明形状 ----
$declared = @(Get-DeclaredIdentities (Join-Path $root 'Projects\Plug\Agent\Config\nodes'))
if ($declared.Count -eq 0) { Add-Violation 'I1' '未找到任何 identity 声明（禁假绿）' }
foreach ($item in $declared) {
    if (-not (Test-IdentityShape $item.Template)) {
        Add-Violation 'I1' ("{0}: identity 形状非单值 '{1}'（多行形态须恰为 '{2}'）" -f $item.File, $item.Template, $canonicalMultiRow)
    }
    $unknown = @(Get-PlaceholderSet $item.Template | Where-Object { $placeholdersAllowed -notcontains $_ })
    if ($unknown.Count -gt 0) {
        Add-Violation 'I4' ("{0}: identity 含未登记占位符 {1}" -f $item.File, ($unknown -join ','))
    }
}
Write-Host ("[FrameIdentity] 声明 {0} 条；多行形态 {1} 条" -f $declared.Count, @($declared | Where-Object { $_.Template -match '\{round' }).Count)

# ---- I2 双向绑定（声明 ⇄ 编译视图）----
$processKeysPath = Join-Path $root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\ProcessKeys.cs'
if (-not (Test-Path -LiteralPath $processKeysPath)) {
    Add-Violation 'I2' '编译视图缺失：ProcessKeys.cs'
}
else {
    $pkText = Get-Content -LiteralPath $processKeysPath -Raw
    $m = [regex]::Match($pkText, 'MultiRowIdentityTemplate\s*=\s*"([^"]+)"')
    if (-not $m.Success) {
        Add-Violation 'I2' 'ProcessKeys.MultiRowIdentityTemplate 未找到（声明↔编译视图绑定不可判）'
    }
    else {
        $compiled = $m.Groups[1].Value
        $declaredMulti = @($declared | Where-Object { $_.Template -match '\{round' } | ForEach-Object { $_.Template } | Sort-Object -Unique)
        if ($declaredMulti.Count -ne 1) {
            Add-Violation 'I2' ("声明面多行身份模板应恰 1 种，实际 {0} 种：{1}" -f $declaredMulti.Count, ($declaredMulti -join ' | '))
        }
        elseif (-not (Test-IdentityShape $compiled)) {
            Add-Violation 'I2' ("编译视图模板形状非单值：'{0}'" -f $compiled)
        }
        else {
            $normalized = $compiled -replace ':[a-zA-Z]+', ''
            if ($normalized -cne $declaredMulti[0]) {
                Add-Violation 'I2' ("声明与编译视图不一致：声明 '{0}' ≠ 编译 '{1}'" -f $declaredMulti[0], $compiled)
            }
            else {
                Write-Host ("[FrameIdentity] 双向绑定 OK：{0}" -f $compiled)
            }
        }
    }

    # ---- I3 ordinal 口径单值 ----
    if (-not (Test-OrdinalModeSingle $pkText)) {
        Add-Violation 'I3' 'ProcessKeys.cs 同时声明「类内到达序」与「轮内全局到达序」两种 ordinal 口径（身份不可复算）'
    }
}

# ---- 负对照（判据自证可判红） ----
if ($NegativeControl) {
    if (Test-IdentityShape '{container}.{kind}.r{round}.{ordinal}') { Add-Violation 'NC' '负对照未命中：段序错误的身份模板被判通过' }
    if (-not (Test-IdentityShape $canonicalMultiRow)) { Add-Violation 'NC' '正对照未命中：规范模板被判非法' }
    if (Test-OrdinalModeSingle '类内到达序 … 轮内全局到达序') { Add-Violation 'NC' '负对照未命中：双口径声明被判单值' }
    if (-not (Test-OrdinalModeSingle '轮内全局到达序')) { Add-Violation 'NC' '正对照未命中：单口径声明被判双口径' }
    Write-Host '[FrameIdentity] 负对照通过（形状/口径均可判红）'
}

if ($violations.Count -gt 0) {
    foreach ($v in $violations) { Write-Host "[VIOLATION] $v" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[FrameIdentity] 通过（0 违规；身份形状单值 + 双向绑定 + ordinal 口径单值）'
if ($Check) { exit 0 }
