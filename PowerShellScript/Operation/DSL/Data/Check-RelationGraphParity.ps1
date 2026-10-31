#requires -Version 5.1
<#
.SYNOPSIS
    关系图三方对账守卫（RelationGraphParity）—— relation-ops / mounts / reference 三份声明必须互相对得上。

.DESCRIPTION
    基线 = Baselines\Data\RelationGraphParity.psd1。检查项：
      RG1 每条 [Relation] intent=ensure 的 edge= 必须在 reference.ushar 的 [Edge] 声明集内；
      RG2 每条 intent=ensure 关系至少被一条 [Mount] relation= 绑定（W2 判据）；
      RG3 每条 [Mount] relation= 必须指向已声明 [Relation]（反向无悬空）；
      RG4 [Mount] 受控字段：stage / family / posture 取值受控、order 为正整数；
      RG5 每条 [Derive] 的 from/to 非空且 via 边均在 [Edge] 声明集内。

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

function Get-DeclarationMap([string]$text, [string]$prefix) {
    # 每条声明：名称 + 续行（缩进体）合并为一行文本（禁缩进推断之外的解析）。
    $map = [ordered]@{}
    $current = $null
    foreach ($line in ($text -split "`r?`n")) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0 -or $trim.StartsWith('#')) { continue }
        if ($trim.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            $rest = $trim.Substring($prefix.Length).Trim()
            $nameEnd = $rest.IndexOf(':')
            if ($nameEnd -le 0) { $current = $null; continue }
            $current = $rest.Substring(0, $nameEnd).Trim()
            $map[$current] = $rest
            continue
        }
        if ($trim.StartsWith('[')) { $current = $null; continue }
        if ($null -ne $current) { $map[$current] = $map[$current] + ' | ' + $trim }
    }
    return $map
}

function Get-Field([string]$line, [string]$name) {
    $m = [regex]::Match($line, '(?:^|\|)\s*' + [regex]::Escape($name) + '\s*=\s*([^|]+)')
    if ($m.Success) { return $m.Groups[1].Value.Trim() }
    return ''
}

function Get-RelationViolations([string]$relationText, [string]$mountText, [string]$edgeText, [hashtable]$controlled) {
    $out = [System.Collections.Generic.List[string]]::new()
    $relations = Get-DeclarationMap $relationText '[Relation]'
    $mounts = Get-DeclarationMap $mountText '[Mount]'
    $edges = Get-DeclarationMap $edgeText '[Edge]'

    $ensure = @{}
    foreach ($name in $relations.Keys) {
        $line = [string]$relations[$name]
        $ensure[$name] = ((Get-Field $line 'intent') -eq 'ensure')
        if (-not $ensure[$name]) { continue }

        $edge = Get-Field $line 'edge'
        if ([string]::IsNullOrWhiteSpace($edge)) {
            $out.Add("RG1 $name 缺 edge= 声明")
        }
        elseif (-not $edges.Contains($edge)) {
            $out.Add("RG1 $name 的边 '$edge' 未在 reference.ushar [Edge] 声明")
        }
    }

    $bound = @{}
    foreach ($mountId in $mounts.Keys) {
        $line = [string]$mounts[$mountId]
        $operation = Get-Field $line 'relation'
        if ([string]::IsNullOrWhiteSpace($operation)) {
            $out.Add("RG3 [Mount] $mountId 缺 relation=")
            continue
        }
        if (-not $relations.Contains($operation)) {
            $out.Add("RG3 [Mount] $mountId 指向未声明关系 '$operation'")
            continue
        }
        $bound[$operation] = $true

        foreach ($field in $controlled.Keys) {
            $value = Get-Field $line $field
            if ($controlled[$field] -notcontains $value) {
                $out.Add("RG4 [Mount] $mountId 的 $field 取值越界：'$value'（受控：$($controlled[$field] -join '|')）")
            }
        }

        $order = Get-Field $line 'order'
        $orderValue = 0
        if (-not [int]::TryParse($order, [ref]$orderValue) -or $orderValue -le 0) {
            $out.Add("RG4 [Mount] $mountId 的 order 须为正整数：'$order'")
        }
    }

    foreach ($name in @($ensure.Keys)) {
        if ($ensure[$name] -and -not $bound.ContainsKey($name)) {
            $out.Add("RG2 ensure 关系 '$name' 无 [Mount] 绑定")
        }
    }

    $derivations = Get-DeclarationMap $relationText '[Derive]'
    foreach ($name in $derivations.Keys) {
        $line = [string]$derivations[$name]
        foreach ($side in @('from', 'to')) {
            if ([string]::IsNullOrWhiteSpace((Get-Field $line $side))) {
                $out.Add("RG5 派生 '$name' 缺 $side=")
            }
        }
        $via = Get-Field $line 'via'
        foreach ($edge in ($via -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
            if (-not $edges.Contains($edge)) {
                $out.Add("RG5 派生 '$name' 的 via 边 '$edge' 未在 [Edge] 声明")
            }
        }
    }

    return $out
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  关系图三方对账守卫（RelationGraphParity）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$relationPath = Join-Path $Root 'Projects\Plug\Agent\Config\relations\relation-ops.ushar'
$mountPath = Join-Path $Root 'Projects\Plug\Agent\Config\relations\mounts.ushar'
$edgePath = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\reference.ushar'
$baselinePath = Join-Path $Root 'Frame\PowerShellScript\Baselines\Data\RelationGraphParity.psd1'

foreach ($path in @($relationPath, $mountPath, $edgePath, $baselinePath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host "[FAIL] 声明面/基线缺失：$path" -ForegroundColor Yellow
        exit 1
    }
}

$baseline = Import-PowerShellDataFile -LiteralPath $baselinePath
$controlled = @{
    stage   = @($baseline.ControlledStage.Value)
    family  = @($baseline.ControlledFamily.Value)
    posture = @($baseline.ControlledPosture.Value)
}

$relationText = Get-Content -LiteralPath $relationPath -Raw
$mountText = Get-Content -LiteralPath $mountPath -Raw
$edgeText = Get-Content -LiteralPath $edgePath -Raw

$violations = Get-RelationViolations -relationText $relationText -mountText $mountText -edgeText $edgeText -controlled $controlled

if ($NegativeControl) {
    # 负样本①：删掉 task_to_history 的 [Mount] ⇒ RG2 必报该 ensure 无绑定；
    # 负样本②：把 mount_task_history 的边改成未声明边 ⇒ RG1 必报。
    $badMounts = (($mountText -split "`r?`n") | Where-Object { $_ -notmatch '^\s*\[Mount\]\s+task_to_history:' }) -join "`n"
    $badRelations = $relationText -replace 'edge=history_owns_task', 'edge=no_such_edge'
    $probe = Get-RelationViolations -relationText $badRelations -mountText $badMounts -edgeText $edgeText -controlled $controlled
    $hitUnbound = @($probe | Where-Object { $_ -like 'RG2*' }).Count -gt 0
    $hitUnknownEdge = @($probe | Where-Object { $_ -like 'RG1*' }).Count -gt 0
    if ($hitUnbound -and $hitUnknownEdge) {
        Write-Host "  [OK] 负对照：删 [Mount] / 换未声明边均被捕获" -ForegroundColor Green
        exit 0
    }
    Write-Host "  [FAIL] 负对照未命中（判据恒真）：unbound=$hitUnbound unknownEdge=$hitUnknownEdge" -ForegroundColor Yellow
    exit 1
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] RelationGraphParity：0 违例" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] RelationGraphParity 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
exit 1

