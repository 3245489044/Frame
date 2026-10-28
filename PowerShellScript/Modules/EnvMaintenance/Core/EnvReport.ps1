# @requires: -
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-report
# @exports: Write-EnvPlanTable, Write-EnvHealthTable, Write-EnvApplyTable, Export-EnvReport

<#
.SYNOPSIS
    报告输出 —— 控制台表格 + JSON/Markdown 落盘。
#>

function Write-EnvPlanTable {
    param([Parameter(Mandatory = $true)][array]$Plan)

    if ($Plan.Count -eq 0) {
        Write-EnvWarn '计划为空（没有匹配的项）'
        return
    }

    Write-Host ''
    Write-Host ("  {0,-22} {1,-14} {2,-10} {3,-8} {4}" -f '项', '类型', '状态', '动作', '当前/目标') -ForegroundColor Yellow
    Write-Host ('  ' + ('-' * 86)) -ForegroundColor DarkGray

    foreach ($step in $Plan) {
        $color = switch ($step.Status) {
            'Ok'       { 'Green' }
            'Outdated' { 'Yellow' }
            'Missing'  { 'Red' }
            'Forced'   { 'Magenta' }
            default    { 'Gray' }
        }
        $values = ''
        if ($step.Current) { $values = [string]$step.Current }
        if ($step.Target) { $values = $values + ' -> ' + $step.Target }

        Write-Host ("  {0,-22} {1,-14} {2,-10} {3,-8} {4}" -f $step.Name, $step.Type, $step.Status, $step.Action, $values) -ForegroundColor $color
    }
    Write-Host ''
}

function Write-EnvHealthTable {
    param([Parameter(Mandatory = $true)]$Health)

    Write-Host ''
    Write-Host ("  环境: {0}" -f $Health.EnvId) -ForegroundColor Cyan
    Write-Host ("  venv: {0}" -f $Health.Venv) -ForegroundColor Gray
    Write-Host ("  python: {0} ({1})" -f $Health.Python, $Health.PythonVersion) -ForegroundColor Gray
    Write-Host ''

    Write-Host ("  {0,-22} {1,-14} {2,-6} {3,-24} {4}" -f '项', '类型', '结果', '版本', '原因') -ForegroundColor Yellow
    Write-Host ('  ' + ('-' * 100)) -ForegroundColor DarkGray

    foreach ($item in $Health.Items) {
        $result = $(if ($item.Ok) { 'OK' } else { 'FAIL' })
        $color = $(if ($item.Ok) { 'Green' } else { 'Red' })
        $version = $(if ($item.Version) { [string]$item.Version } else { '' })
        Write-Host ("  {0,-22} {1,-14} {2,-6} {3,-24} {4}" -f $item.Name, $item.Type, $result, $version, $item.Reason) -ForegroundColor $color
    }
    Write-Host ''

    if ($Health.Ok) { Write-EnvOk '体检通过' } else { Write-EnvFail '体检未通过' }
}

function Write-EnvApplyTable {
    param([Parameter(Mandatory = $true)]$Report)

    if ($Report.Applied.Count -gt 0) { Write-EnvOk ("已应用: " + ($Report.Applied -join ', ')) }
    if ($Report.Failed.Count -gt 0) { Write-EnvFail ("失败: " + ($Report.Failed -join ', ')) }

    $bad = @($Report.Verifications | Where-Object { -not $_.Ok })
    if ($bad.Count -gt 0) {
        foreach ($item in $bad) { Write-EnvFail ("复核失败 {0}: {1}" -f $item.Name, $item.Reason) }
    } elseif ($Report.Verifications.Count -gt 0) {
        Write-EnvOk '复核通过'
    }

    if ($Report.RolledBack) { Write-EnvWarn '已回滚依赖快照' }
}

function Export-EnvReport {
    <#
    .SYNOPSIS
        报告落盘：JSON 或 Markdown。
    #>
    param(
        [Parameter(Mandatory = $true)]$Report,
        [Parameter(Mandatory = $true)][string]$Path,
        [ValidateSet('Json', 'Markdown')][string]$Format = 'Json'
    )

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

    if ($Format -eq 'Json') {
        $json = $Report | ConvertTo-Json -Depth 8
        Set-Content -LiteralPath $Path -Value $json -Encoding UTF8
        return $Path
    }

    $lines = @()
    $lines += "# EnvMaintenance 报告 — $($Report.EnvId)"
    $lines += ''
    $lines += "- 生成时间: $($Report.CheckedAt)"
    $lines += "- 清单: $($Report.ManifestPath)"
    $lines += "- 结果: $(if ($Report.Ok) { 'PASS' } else { 'FAIL' })"
    $lines += ''
    $lines += '| 项 | 类型 | 结果 | 版本 | 原因 |'
    $lines += '|---|---|---|---|---|'
    foreach ($item in $Report.Items) {
        $result = $(if ($item.Ok) { 'PASS' } else { 'FAIL' })
        $lines += "| $($item.Name) | $($item.Type) | $result | $($item.Version) | $($item.Reason) |"
    }
    Set-Content -LiteralPath $Path -Value ($lines -join "`r`n") -Encoding UTF8
    return $Path
}
