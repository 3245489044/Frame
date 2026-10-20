# PlanRunner.psm1 — 能力编排原语（run.plan）
# 单一职责：按 steps[] 顺序调用既有契约（文件化能力），收集逐步结果；不生成代码、不建临时脚本。
# 复用边界：能力体只在 .psm1 文件里（同基名 .contracts.json），本文件只做编排。

function ConvertTo-PsParamTable {
    param([AllowNull()]$Value)
    $table = @{}
    if ($null -eq $Value) { return $table }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($k in $Value.Keys) { $table[[string]$k] = $Value[$k] }
        return $table
    }
    if ($Value -is [pscustomobject]) {
        foreach ($p in $Value.PSObject.Properties) { $table[$p.Name] = $p.Value }
        return $table
    }
    return $table
}

function Test-PsWriteAction {
    param([string]$Action)
    if ([string]::IsNullOrWhiteSpace($Action)) { return $false }
    return ($Action -match '^(write|replace|store|delete)\.')
}

function Get-PsResultPath {
    param($Data)
    if ($null -eq $Data) { return $null }
    if ($Data -is [System.Collections.IDictionary]) {
        if ($Data.Contains('Path')) { return [string]$Data['Path'] }
        return $null
    }
    if ($Data.PSObject.Properties.Name -contains 'Path') { return [string]$Data.Path }
    return $null
}

function Invoke-CapabilityPlan {
    param(
        [Parameter(Mandatory)][AllowNull()]$Steps,
        [AllowNull()][array]$Contracts,
        [string]$Mode = 'write',
        [switch]$FailFast
    )

    $results = [System.Collections.Generic.List[object]]::new()
    $errors = @()
    $artifacts = @()
    $index = 0

    foreach ($step in @($Steps)) {
        if ($null -eq $step) { continue }
        $index++
        $id = if ($step.id) { [string]$step.id } else { "step$index" }
        $action = [string]$step.action
        if ([string]::IsNullOrWhiteSpace($action)) {
            $errors += "step '$id' 缺 action"
            $results.Add([PSCustomObject]@{ id = $id; action = ''; ok = $false; status = 'failure'; message = 'missing_action'; elapsedMs = 0 })
            if ($FailFast) { break }
            continue
        }

        if ($Mode -eq 'readonly' -and (Test-PsWriteAction $action)) {
            $errors += "readonly 拒绝写动作: $action"
            $results.Add([PSCustomObject]@{ id = $id; action = $action; ok = $false; status = 'failure'; message = 'readonly_denied'; elapsedMs = 0 })
            if ($FailFast) { break }
            continue
        }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $r = Invoke-PsAction -Action $action -Params (ConvertTo-PsParamTable $step.params) -Contracts $Contracts
        } catch {
            $r = New-PsResult -Status failure -Message $_.Exception.Message -Errors @($_.Exception.Message)
        }
        $sw.Stop()

        $ok = ([string]$r.status -eq 'success')
        $results.Add([PSCustomObject]@{
            id        = $id
            action    = $action
            ok        = $ok
            status    = [string]$r.status
            message   = [string]$r.message
            data      = $r.data
            errors    = @($r.errors)
            elapsedMs = [int]$sw.Elapsed.TotalMilliseconds
        })

        $path = Get-PsResultPath $r.data
        if ($ok -and $path) { $artifacts += $path }

        if (-not $ok) {
            $errors += "$action : $($r.message)"
            if ($FailFast) { break }
        }
    }

    $status = if ($errors.Count -eq 0) { 'success' } else { 'failure' }
    return New-PsResult -Status $status -Message "plan steps=$($results.Count)" -Data @{
        status    = $status
        steps     = @($results.ToArray())
        artifacts = @($artifacts)
        errors    = @($errors)
    } -Errors @($errors)
}

Export-ModuleMember -Function Invoke-CapabilityPlan
