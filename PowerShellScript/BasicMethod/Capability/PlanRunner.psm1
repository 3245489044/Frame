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
    param([string]$Action, [AllowNull()][array]$Contracts)
    if ([string]::IsNullOrWhiteSpace($Action)) { return $false }

    # DUP-06：写属性唯一判据 = 契约 identity.verb（与 C# VerbSemantics 同源）；未命中契约才回退前缀规则。
    foreach ($c in @($Contracts)) {
        if ($null -eq $c -or $null -eq $c.identity) { continue }
        if ([string]$c.identity.action -ieq $Action) {
            $verb = [string]$c.identity.verb
            return ($verb -in @('write', 'replace', 'store', 'delete', 'patch'))
        }
    }

    $prefix = ($Action -split '\.')[0]
    return ($prefix -in @('write', 'replace', 'store', 'delete', 'patch'))
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
        [switch]$FailFast,
        # A8：计划级软预算（秒）。0=不限；上限 600。宿主单次调用仍有 60s 硬超时兜底，
        # 软预算在每步边界自检 ⇒ 超时也能带回已完成步骤（不再「整体 timeout 零进度」）。
        [int]$TimeoutSeconds = 45
    )

    $results = [System.Collections.Generic.List[object]]::new()
    $errors = @()
    $artifacts = @()
    $usage = @{}
    $remaining = @()
    $timedOut = $false
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $budgetMs = if ($TimeoutSeconds -gt 0) { [int]([Math]::Min($TimeoutSeconds, 600) * 1000) } else { 0 }
    $stepList = @($Steps | Where-Object { $null -ne $_ })
    $index = 0

    for ($i = 0; $i -lt $stepList.Count; $i++) {
        if ($budgetMs -gt 0 -and $clock.ElapsedMilliseconds -ge $budgetMs) {
            $timedOut = $true
            for ($j = $i; $j -lt $stepList.Count; $j++) { $remaining += [string]$stepList[$j].action }
            break
        }

        $step = $stepList[$i]
        $index++
        $id = if ($step.id) { [string]$step.id } else { "step$index" }
        $action = [string]$step.action
        if ([string]::IsNullOrWhiteSpace($action)) {
            $errors += "step '$id' 缺 action"
            $results.Add([PSCustomObject]@{ id = $id; action = ''; ok = $false; status = 'failure'; message = 'missing_action'; elapsedMs = 0 })
            if ($FailFast) { break }
            continue
        }

        if ($Mode -eq 'readonly' -and (Test-PsWriteAction $action $Contracts)) {
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
        $usage[$action] = 1 + [int]$usage[$action]

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

        if ($budgetMs -gt 0 -and $clock.ElapsedMilliseconds -ge $budgetMs -and ($i + 1) -lt $stepList.Count) {
            $timedOut = $true
            for ($j = $i + 1; $j -lt $stepList.Count; $j++) { $remaining += [string]$stepList[$j].action }
            break
        }
    }

    # ERR-03：顶层 status 三态 —— partial 是「部分完成」的一等值，不得压平为 failure。
    $status = if ($timedOut) { 'partial' } elseif ($errors.Count -eq 0) { 'success' } else { 'failure' }
    $suffix = if ($timedOut) { ' timeout' } else { '' }
    return New-PsResult -Status $status -Message "plan steps=$($results.Count)/$($stepList.Count)$suffix" -Data @{
        status    = $status
        steps     = @($results.ToArray())
        artifacts = @($artifacts)
        errors    = @($errors)
        usage     = $usage
        timedOut  = $timedOut
        remaining = @($remaining)
    } -Errors @($errors)
}

Export-ModuleMember -Function Invoke-CapabilityPlan
