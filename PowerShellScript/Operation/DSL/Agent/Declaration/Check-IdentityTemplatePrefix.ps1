#requires -Version 5.1
<#
.SYNOPSIS
    身份模板声明门禁（DTC-6）：identityTemplate 引用可解析 ∧ 模板封闭 ∧ 前缀可判定。
.DESCRIPTION
    声明面单源 = Projects/Plug/Agent/Config/instances/*.json；模板本体经 {file}#{field} 引用解析
    （不在实例文件复制模板文本）。
    判据（结构事实）：
      IT1 引用必须形如 "{file}#{field}" 且目标文件/字段存在；
      IT2 模板值非空 ∧ 花括号配对（占位符封闭）；
      IT3 前缀可判定：要么以字面量起始，要么**整体恰为单个占位符**（如 "{container}"）；
          「占位符后仍拼字面量」= 前缀依赖渲染期值 ⇒ 不可判定（红）。
    模式：默认报告；-Check 违规 → 退出码 1。只读仓库，零侧写。
#>
[CmdletBinding()]
param([switch]$Check)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

$configRoot = Join-Path $repoRoot 'Projects\Plug\Agent\Config'
$instanceRoot = Join-Path $configRoot 'instances'
$violations = [System.Collections.Generic.List[string]]::new()
function Add-Violation([string]$Code, [string]$Message) {
    $violations.Add("$Code $Message")
    Write-Host "  [RED] $Code $Message" -ForegroundColor Red
}

if (-not (Test-Path -LiteralPath $instanceRoot)) {
    Write-Host "  [SKIP] 实例声明目录缺失（干净 checkout）: $instanceRoot" -ForegroundColor Yellow
}
else {
    foreach ($file in Get-ChildItem -Path $instanceRoot -File -Filter '*.json') {
        $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $reference = $doc.identityTemplate
        if ([string]::IsNullOrWhiteSpace($reference)) { continue }

        if ($reference -notmatch '^(?<file>[^#]+)#(?<field>[A-Za-z0-9_]+)$') {
            Add-Violation 'IT1' "$($file.Name): identityTemplate 非 {file}#{field} 形态: $reference"
            continue
        }

        $targetFile = Join-Path $configRoot $Matches['file']
        $field = $Matches['field']
        if (-not (Test-Path -LiteralPath $targetFile)) {
            Add-Violation 'IT1' "$($file.Name): 引用目标不存在: $($Matches['file'])"
            continue
        }

        $target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json
        $prop = $target.PSObject.Properties[$field]
        if ($null -eq $prop -or [string]::IsNullOrWhiteSpace([string]$prop.Value)) {
            Add-Violation 'IT1' "$($file.Name): 引用字段缺失/为空: $field"
            continue
        }

        $template = [string]$prop.Value
        $open = ([regex]::Matches($template, '\{')).Count
        $close = ([regex]::Matches($template, '\}')).Count
        if ($open -ne $close) {
            Add-Violation 'IT2' "$($file.Name): 模板花括号不配对: $template"
            continue
        }

        $singlePlaceholder = $template -match '^\{[A-Za-z0-9_]+\}$'
        $literalStart = $template -match '^[^\{]'
        if (-not ($singlePlaceholder -or $literalStart)) {
            # IT3 仅约束「身份 = 前台业务键」的 Kind（session/history/task）；
            # node 身份 = 位置键派生（如 {container}.r{round}…），由位置声明承担，不在此判据域。
            if ($doc.kind -in @('session', 'history', 'task')) {
                Add-Violation 'IT3' "$($file.Name): 前缀不可判定（占位符后拼字面量）: $template"
            }
        }
    }
}

if ($violations.Count -eq 0) {
    Write-Host "  [OK] identityTemplate：引用可解析 ∧ 模板封闭 ∧ 前缀可判定" -ForegroundColor Green
}

if ($Check -and $violations.Count -gt 0) { exit 1 }
