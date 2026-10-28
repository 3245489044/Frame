#requires -Version 5.1
<#
.SYNOPSIS
    图边声明完整性守卫（GraphDeclarationParity）—— 参与图写的 [Edge] 必须五字段齐备且取值受控。

.DESCRIPTION
    基线 = Baselines\Data\GraphDeclarationParity.psd1。检查项：
      GD1 必需边集（node_to_node / Owns / cross_db_link / session_owns / task_owns_task / history_owns_task）
          必须各有 [Edge] 声明，且 identity/tombstone/unique/revive/cascade 五字段齐备；
      GD2 字段取值 ∈ 受控词（identity: source_target_ref|source_target_ref_location；tombstone: durable|none；
          unique: true|false；revive: explicit|attach|never；cascade: declaration|edge|none）；
      GD3 规则表 then.action ∈ 受控四态（Write / ClearTombstoneThenWrite / SkipAndReport / Deny）。

    违例 → 退出码 1；-Check 为判定模式（缺省同判定）；-NegativeControl 跑合成负样本（必须判红）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$required = @('node_to_node', 'Owns', 'cross_db_link', 'session_owns', 'task_owns_task', 'history_owns_task')
$fields = @{
    identity  = @('source_target_ref', 'source_target_ref_location')
    tombstone = @('durable', 'none')
    unique    = @('true', 'false')
    revive    = @('explicit', 'attach', 'never')
    cascade   = @('declaration', 'edge', 'none')
}
$actions = @('Write', 'ClearTombstoneThenWrite', 'SkipAndReport', 'Deny')

function Get-EdgeDeclarations([string]$text) {
    # 每条 [Edge]：名称 + 续行（缩进体）合并为一行文本（禁缩进推断之外的解析）。
    $map = @{}
    $current = $null
    foreach ($line in ($text -split "`r?`n")) {
        $trim = $line.Trim()
        if ($trim.StartsWith('[Edge]', [System.StringComparison]::Ordinal)) {
            $rest = $trim.Substring(6).Trim()
            $nameEnd = $rest.IndexOf(':')
            if ($nameEnd -le 0) { $current = $null; continue }
            $current = $rest.Substring(0, $nameEnd).Trim()
            $map[$current] = $rest
            continue
        }
        if ($trim.StartsWith('[')) { $current = $null; continue }
        if ($null -ne $current -and $trim.Length -gt 0) {
            $map[$current] = $map[$current] + ' | ' + $trim
        }
    }
    return $map
}

function Get-DeclarationViolations([string]$edgeText, [string]$rulesText) {
    $out = [System.Collections.Generic.List[string]]::new()
    $edges = Get-EdgeDeclarations $edgeText

    foreach ($name in $required) {
        if (-not $edges.ContainsKey($name)) {
            $out.Add("GD1 必需边缺 [Edge] 声明：$name")
            continue
        }
        $line = [string]$edges[$name]
        foreach ($field in $fields.Keys) {
            $m = [regex]::Match($line, '(?:^|\|)\s*' + [regex]::Escape($field) + '\s*=\s*([^|]+)')
            if (-not $m.Success) {
                $out.Add("GD1 $name 缺策略字段：$field")
                continue
            }
            $value = $m.Groups[1].Value.Trim()
            if ($fields[$field] -notcontains $value) {
                $out.Add("GD2 $name 的 $field 取值越界：$value")
            }
        }
    }

    foreach ($m in [regex]::Matches($rulesText, '(?m)^\s*then\.action\s*=\s*([A-Za-z]+)\s*$')) {
        $action = $m.Groups[1].Value
        if ($actions -notcontains $action) {
            $out.Add("GD3 规则动作越界：$action")
        }
    }

    return $out
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  图边声明完整性守卫（GraphDeclarationParity）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$edgePath = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\reference.ushar'
$rulePath = Join-Path $Root 'Projects\Plug\Agent\Config\rules\graph-policy.rules.ushar'
if (-not (Test-Path $edgePath)) { Write-Host "[FAIL] 声明面缺失：$edgePath" -ForegroundColor Yellow; exit 1 }
$edgeText = Get-Content -LiteralPath $edgePath -Raw
$rulesText = if (Test-Path $rulePath) { Get-Content -LiteralPath $rulePath -Raw } else { '' }

$violations = Get-DeclarationViolations -edgeText $edgeText -rulesText $rulesText

if ($NegativeControl) {
    $badEdge = $edgeText -replace '(?m)(^\[Edge\] node_to_node:.*?)\|\s*tombstone=[a-z]+\s*\|', '$1|'
    $badRules = $rulesText + "`n[Rule] r-bad`n  then.action = Explode`n"
    $probe = Get-DeclarationViolations -edgeText $badEdge -rulesText $badRules
    $hitMissing = @($probe | Where-Object { $_ -like 'GD1 node_to_node 缺策略字段*' }).Count -gt 0
    $hitAction = @($probe | Where-Object { $_ -like 'GD3*' }).Count -gt 0
    if ($hitMissing -and $hitAction) {
        Write-Host "  [OK] 负对照：删字段/越界动作均被捕获" -ForegroundColor Green
        exit 0
    }
    Write-Host "  [FAIL] 负对照未命中（判据恒真）：missing=$hitMissing action=$hitAction" -ForegroundColor Yellow
    exit 1
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] GraphDeclarationParity：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] GraphDeclarationParity 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
exit 1
