# @requires: Core/EnvProviders, Core/EnvManifest, Core/PipRunner
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-plan
# @exports: New-EnvPlan, Invoke-EnvPlan, Test-EnvManifestHealth, Save-EnvPipSnapshot, Restore-EnvPipSnapshot

<#
.SYNOPSIS
    plan → apply → verify → rollback 管道。
.DESCRIPTION
    01 生成计划（Test 探测，不写盘）
    02 apply 前 pip freeze 快照（可回滚）
    03 按清单拓扑序执行 Install
    04 逐项复核（状态 Test + verify 断言）
    05 复核失败且有快照 → 回滚 pip 依赖
#>

function New-EnvPlan {
    <#
    .SYNOPSIS
        计算维护计划（只读探测）。
    #>
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [int]$Phase = 0,
        [string]$Name = '',
        [switch]$Force
    )

    $steps = @()
    foreach ($item in (Get-EnvManifestItemOrder -Manifest $Manifest)) {
        if ($Phase -gt 0 -and [int]$item.phase -ne $Phase) { continue }
        if ($Name -and ([string]$item.name) -ne $Name) { continue }

        $test = Test-EnvItem -Item $item -Manifest $Manifest

        $status = 'Ok'
        $action = 'Skip'
        if ($Force) {
            $status = 'Forced'
            $action = 'Apply'
        } elseif (-not $test.Installed) {
            $status = $(if ($test.Outdated) { 'Outdated' } else { 'Missing' })
            $action = 'Apply'
        }

        $steps += [pscustomobject]@{
            Name    = [string]$item.name
            Type    = [string]$item.type
            Phase   = [int]$item.phase
            Status  = $status
            Action  = $action
            Current = $test.Version
            Target  = $(if ($item.version) { [string]$item.version } else { '' })
            Reason  = $test.Reason
            Item    = $item
        }
    }

    return $steps
}

function Save-EnvPipSnapshot {
    param([Parameter(Mandatory = $true)]$Manifest, [Parameter(Mandatory = $true)][string]$Python)

    if (-not (Test-Path -LiteralPath $Python)) { return '' }

    $dir = Join-Path (Get-EnvStateRoot -Manifest $Manifest) ([string]$Manifest.envId)
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $path = Join-Path $dir 'requirements.lock.txt'

    $logPath = Get-EnvPipLogPath -EnvName ([string]$Manifest.envId) -Tag 'freeze' -Manifest $Manifest
    $result = Invoke-EnvPip -Python $Python -PipArgs @('freeze') -TimeoutSeconds 120 -LogPath $logPath -Quiet
    if ($result.ExitCode -ne 0) { return '' }

    Set-Content -LiteralPath $path -Value $result.Output -Encoding UTF8
    return $path
}

function Restore-EnvPipSnapshot {
    param([Parameter(Mandatory = $true)]$Manifest, [Parameter(Mandatory = $true)][string]$Python, [Parameter(Mandatory = $true)][string]$Snapshot)

    if (-not (Test-Path -LiteralPath $Snapshot)) { return $false }
    Write-EnvWarn "回滚依赖快照: $Snapshot"
    $logPath = Get-EnvPipLogPath -EnvName ([string]$Manifest.envId) -Tag 'rollback' -Manifest $Manifest
    $result = Invoke-EnvPipRequirements -Python $Python -Requirements $Snapshot -TimeoutSeconds 1800 -LogPath $logPath -Quiet
    if ($result.ExitCode -ne 0) {
        Write-EnvFail "回滚失败: $($result.Reason)"
        return $false
    }
    Write-EnvOk '依赖已回滚到快照'
    return $true
}

function Invoke-EnvPlan {
    <#
    .SYNOPSIS
        执行维护计划并复核；返回报告对象。
    #>
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [int]$Phase = 0,
        [string]$Name = '',
        [switch]$Force,
        [switch]$WhatIf,
        [switch]$NoRollback,
        [switch]$SkipVerify
    )

    $plan = @(New-EnvPlan -Manifest $Manifest -Phase $Phase -Name $Name -Force:$Force)
    $toApply = @($plan | Where-Object { $_.Action -eq 'Apply' })

    $report = [pscustomobject]@{
        EnvId        = [string]$Manifest.envId
        ManifestPath = [string]$Manifest.manifestPath
        GeneratedAt  = (Get-Date)
        WhatIf       = [bool]$WhatIf
        Plan         = $plan
        Applied      = @()
        Failed       = @()
        Verifications = @()
        Snapshot     = ''
        RolledBack   = $false
        Ok           = $false
    }

    if ($WhatIf) {
        $report.Ok = ($toApply.Count -eq 0)
        return $report
    }

    # 快照（仅当将要改动 pip 项）
    $pipItems = @($toApply | Where-Object { $_.Type -in @('pip-venv', 'pip-req-venv') })
    $venv = Get-EnvManifestVenv -Manifest $Manifest
    if ($pipItems.Count -gt 0 -and $venv) {
        $venvPython = Resolve-VenvPython -VenvDir $venv
        if (Test-Path -LiteralPath $venvPython) {
            $report.Snapshot = Save-EnvPipSnapshot -Manifest $Manifest -Python $venvPython
            if ($report.Snapshot) { Write-EnvInfo '依赖快照' $report.Snapshot }
        }
    }

    $applied = @()
    $failed = @()
    foreach ($step in $toApply) {
        Write-EnvStep "执行 $($step.Name) [$($step.Type)]"
        if (Invoke-EnvItemApply -Item $step.Item -Manifest $Manifest) {
            $applied += $step.Name
        } else {
            $failed += $step.Name
            break
        }
    }
    $report.Applied = $applied
    $report.Failed = $failed

    $verifications = @()
    if (-not $SkipVerify) {
        foreach ($step in $plan) {
            $state = Test-EnvItem -Item $step.Item -Manifest $Manifest
            $assert = Test-EnvItemVerify -Item $step.Item -Manifest $Manifest
            $ok = ($state.Installed -and $assert.Ok)
            $reason = $state.Reason
            if (-not $assert.Ok -and $assert.Reason) { $reason = $assert.Reason }
            $verifications += [pscustomobject]@{
                Name    = $step.Name
                Ok      = [bool]$ok
                Version = $state.Version
                Reason  = $reason
            }
        }
    }
    $report.Verifications = $verifications

    $allOk = ($failed.Count -eq 0)
    if (-not $SkipVerify) {
        $failedVerify = @($verifications | Where-Object { -not $_.Ok })
        if ($failedVerify.Count -gt 0) { $allOk = $false }
    }
    $report.Ok = $allOk

    if (-not $allOk -and $report.Snapshot -and -not $NoRollback) {
        $rollbackPython = $venv
        if ($rollbackPython) {
            $report.RolledBack = Restore-EnvPipSnapshot -Manifest $Manifest -Python (Resolve-VenvPython -VenvDir $venv) -Snapshot $report.Snapshot
        }
    }

    return $report
}

function Test-EnvManifestHealth {
    <#
    .SYNOPSIS
        只读体检：不安装、不写标记，逐项 Test + verify 断言。
    #>
    param([Parameter(Mandatory = $true)]$Manifest)

    $venv = Get-EnvManifestVenv -Manifest $Manifest
    $python = ''
    $pythonVersion = $null
    if ($venv) {
        $python = Resolve-VenvPython -VenvDir $venv
        if (Test-Path -LiteralPath $python) { $pythonVersion = Get-EnvPythonVersion -Python $python -Manifest $Manifest }
    }

    $items = @()
    $allOk = $true
    foreach ($item in (Get-EnvManifestItemOrder -Manifest $Manifest)) {
        $state = Test-EnvItem -Item $item -Manifest $Manifest
        $assert = Test-EnvItemVerify -Item $item -Manifest $Manifest
        $ok = ($state.Installed -and $assert.Ok)
        if (-not $ok) { $allOk = $false }

        $reason = $state.Reason
        if (-not $assert.Ok -and $assert.Reason) { $reason = $assert.Reason }

        $items += [pscustomobject]@{
            Name    = [string]$item.name
            Type    = [string]$item.type
            Phase   = [int]$item.phase
            Ok      = [bool]$ok
            Version = $state.Version
            Reason  = $reason
        }
    }

    return [pscustomobject]@{
        EnvId         = [string]$Manifest.envId
        ManifestPath  = [string]$Manifest.manifestPath
        Venv          = $venv
        Python        = $python
        PythonVersion = $pythonVersion
        Ok            = [bool]$allOk
        Items         = $items
        CheckedAt     = (Get-Date)
    }
}
