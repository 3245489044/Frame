<#
    .SYNOPSIS
        对账 / 健康判据的 PowerShell 侧内核（门禁侧消费方）。
    .DESCRIPTION
        动机（节点装配事故）：三面（声明 / 派发 / 装配）各自「有数据」不等于一致；
        旧判定把空集求和当 0 错误 ⇒ 全绿。本模块只做两件事：
          · 多面对账：委托唯一比较/覆盖实现 = C# `FaceReconciler`（经 LedgerForge `reconcile-faces`）；
          · 健康判定：fail-closed（空 / 未装载 / 未解析 / attached=0 / 诊断未聚合 ⇒ 红）。
        业务面清单与阈值由调用门传入；本模块零域词、零硬编码路径。
    .NOTES
        用法：
            Import-Module (Join-Path $PSScriptRoot 'GateFaceReconciliation.psm1') -Force -DisableNameChecking
            $report = Get-GateFaceReconciliation -Faces ([ordered]@{ declared = $declaredKeys; dispatched = ...; assembled = ... }) -Reference 'declared'
            $verdict = Get-GateHealthVerdict -Criteria @(@{ Name='attached'; State='Loaded'; Count=3 }) -Errors 0 -Warnings 0 -Counters @{ attached=3 } -Reconciliation $report
            Assert-GateFaceControls -Violations $ctx.Violations
    .NOTES
        C# 同口径实现 = BasicMethod.Declarative.Ledger.FaceReconciler / FailClosedHealthAdjudicator / CriterionInputValidator。
#>

# 违规追加 / 对象取数 / 工具定位：复用既有单源，不复制第二实现。
Import-Module (Join-Path $PSScriptRoot 'GateRuntime.psm1') -DisableNameChecking

<#
.SYNOPSIS
    多面对账（声明面 ↔ 派发面 ↔ 装配面）：每一面实际数量、覆盖等级、缺失项。
.DESCRIPTION
    委托 LedgerForge `reconcile-faces`（唯一判定实现 = C# FaceReconciler）。
    `-Faces` 值可为：键集字符串数组；或已抽取的键集 JSON 路径（字符串）。
    **空面 / 文件缺失 / 不可读 ⇒ 覆盖 empty/unknown ⇒ Ok=false**（空集不是「无需对账」）。
.PARAMETER Faces
    有序面表（名 → 键集数组 | 键集 JSON 路径）；至少两面。
.PARAMETER Reference
    参照面名（缺省 = 首个面）；显式时边 = ref → 其余面。
.PARAMETER RepoRoot
    仓库根（缺省 = GateRuntime 自定位）。
.OUTPUTS
    [pscustomobject] Ok / Coverage / Reference / Faces / Edges / TerminalMissing / Reasons / ExitCode
#>
function Get-GateFaceReconciliation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Faces,
        [string]$Reference = '',
        [string]$RepoRoot = ''
    )

    if ($Faces.Count -lt 2) { throw 'reconcile-faces 至少需要两个面（空输入不是「无需对账」）' }

    $ledgerForge = Get-GateLedgerForge -RepoRoot $RepoRoot
    if (-not $ledgerForge) { throw 'LedgerForge 未构建（缺 BuildOutput\Debug|Release\LedgerForge.exe），多面对账不可执行' }

    $probeDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('faces-' + [guid]::NewGuid().ToString('N')))
    try {
        New-Item -ItemType Directory -Force -Path $probeDir | Out-Null
        $faceArgs = [System.Collections.Generic.List[string]]::new()
        $index = 0
        foreach ($name in @($Faces.Keys)) {
            $value = $Faces[$name]
            $index++
            if ($value -is [string]) {
                $path = $value
                if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                    # 文件缺失不是「空集」：缺文件交给 CLI 判 loaded=false（红）。
                    $path = Join-Path $probeDir ("missing-$index.json")
                }
            } else {
                $path = Join-Path $probeDir ("face-$index.json")
                $items = @()
                foreach ($item in @($value)) {
                    if (-not [string]::IsNullOrEmpty([string]$item)) { $items += [string]$item }
                }
                $json = '[' + (($items | ForEach-Object { '"' + ($_.Replace('\', '\\').Replace('"', '\"')) + '"' }) -join ',') + ']'
                [System.IO.File]::WriteAllText($path, $json, [System.Text.Encoding]::UTF8)
            }

            $faceArgs.Add('--face')
            $faceArgs.Add(("{0}={1}" -f $name, $path))
        }

        if (-not [string]::IsNullOrWhiteSpace($Reference)) {
            $faceArgs.Add('--ref')
            $faceArgs.Add($Reference)
        }

        $outPath = Join-Path $probeDir 'result.json'
        $faceArgs.Add('--out')
        $faceArgs.Add($outPath)

        & $ledgerForge reconcile-faces @($faceArgs) | Out-Null
        $code = $LASTEXITCODE
        if ($code -eq 2 -or -not (Test-Path -LiteralPath $outPath)) {
            throw "reconcile-faces 不可执行（退出码 $code）"
        }

        $payload = Get-Content -LiteralPath $outPath -Raw -Encoding UTF8 | ConvertFrom-Json
        return [pscustomobject]@{
            Ok              = [bool]$payload.ok
            Coverage        = [string]$payload.coverage
            Reference       = [string]$payload.reference
            Faces           = @($payload.faces)
            Edges           = @($payload.edges)
            TerminalMissing = @($payload.terminalMissing)
            Reasons         = @($payload.reasons)
            ExitCode        = $code
        }
    }
    finally {
        if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Recurse -Force }
    }
}

<#
.SYNOPSIS
    判据输入校验：空集合 / 未装载 / 未解析 / 隐式跳过 ⇒ 红；显式不适用才可排除。
.OUTPUTS
    [pscustomobject] Name / Verdict（Accepted|Rejected|NotApplicable|Unverifiable）/ Reason
#>
function Test-GateCriterionInput {
    [CmdletBinding()]
    param(
        [string]$Name = '',
        [ValidateSet('Loaded', 'Empty', 'Unloaded', 'Unresolved', 'NotApplicableDeclared', 'NotApplicableSilent')]
        [string]$State = 'Loaded',
        [int]$Count = 0
    )

    $label = if ([string]::IsNullOrWhiteSpace($Name)) { '<unnamed>' } else { $Name.Trim() }
    switch ($State) {
        'Loaded' {
            if ($Count -gt 0) {
                return [pscustomobject]@{ Name = $label; Verdict = 'Accepted'; Reason = "${label}: 已装载且标的 $Count 项" }
            }
            return [pscustomobject]@{ Name = $label; Verdict = 'Rejected'; Reason = "${label}: 已装载但标的为空（空集不是「无需判定」⇒ 红）" }
        }
        'Empty' { return [pscustomobject]@{ Name = $label; Verdict = 'Rejected'; Reason = "${label}: 判据集合为空（无标的 ⇒ 红）" } }
        'Unloaded' { return [pscustomobject]@{ Name = $label; Verdict = 'Unverifiable'; Reason = "${label}: 未装载（不可判定 ⇒ 红）" } }
        'Unresolved' { return [pscustomobject]@{ Name = $label; Verdict = 'Unverifiable'; Reason = "${label}: 解析失败（不可判定 ⇒ 红）" } }
        'NotApplicableDeclared' { return [pscustomobject]@{ Name = $label; Verdict = 'NotApplicable'; Reason = "${label}: 显式声明不适用（不计入判定）" } }
        default { return [pscustomobject]@{ Name = $label; Verdict = 'Rejected'; Reason = "${label}: 隐式跳过（未显式声明不适用 ⇒ 红）" } }
    }
}

<#
.SYNOPSIS
    fail-closed 健康判定（门禁侧）。
.DESCRIPTION
    规则与 C# FailClosedHealthAdjudicator 一一对应：
      1. 诊断未聚合 ⇒ 红；Errors/Warnings 未提供 ⇒ 红（空集求和不可采信）。
      2. Errors > 0 ⇒ 红；Warnings > 0 且 WarningsBlock ⇒ 红。
      3. RequiredPositiveCounters 缺失 / ≤ 0（如 attached=0）⇒ 红。
      4. 判据输入空 / Empty / Unloaded / Unresolved / 隐式跳过 ⇒ 红；显式不适用才排除。
      5. 对账未提供且 RequireReconciliation ⇒ 红；对账 Ok=false ⇒ 红（透传其 Reesons）。
      6. 只有 Green 放行；全部判据显式不适用 ⇒ NotApplicable（不放行也不假装通过）。
.PARAMETER Criteria
    判据数组：@{ Name; State; Count }。
.PARAMETER Errors
    错误计数；**不传 = 未提供 ⇒ 红**（不要用缺省 0 冒充「零错误」）。
.PARAMETER Warnings
    警告计数；同样不得缺省为 0。
.PARAMETER Counters
    关键计数（@{ attached = 3 }）。
.PARAMETER RequiredPositiveCounters
    必须为正的计数名（如 attached）。
.PARAMETER Reconciliation
    Get-GateFaceReconciliation 的返回（可为 $null）。
.OUTPUTS
    [pscustomobject] Verdict（Green|Red|NotApplicable）/ Reasons / AcceptedCriteria / IsGreen
#>
function Get-GateHealthVerdict {
    [CmdletBinding()]
    param(
        [object[]]$Criteria = @(),
        [AllowNull()][System.Nullable[int]]$Errors = $null,
        [AllowNull()][System.Nullable[int]]$Warnings = $null,
        [System.Collections.IDictionary]$Counters = @{},
        [string[]]$RequiredPositiveCounters = @(),
        [object]$Reconciliation = $null,
        [bool]$WarningsBlock = $true,
        [bool]$RequireReconciliation = $true,
        [bool]$DiagnosticsAggregated = $true,
        [string]$DiagnosticsScope = ''
    )

    $reasons = [System.Collections.Generic.List[string]]::new()
    $accepted = [System.Collections.Generic.List[string]]::new()
    $notApplicable = 0

    if (-not $DiagnosticsAggregated) {
        $reasons.Add('诊断未在完整面聚合（不可由空集求和推导 0 错误 ⇒ 红）')
    }
    elseif ($null -eq $Errors -or $null -eq $Warnings) {
        $reasons.Add('错误/警告计数未提供（不得缺省为 0 ⇒ 红）')
    }
    else {
        if ([int]$Errors -lt 0 -or [int]$Warnings -lt 0) {
            $reasons.Add('错误/警告计数为负（不可判定 ⇒ 红）')
        }
        else {
            if ([int]$Errors -gt 0) { $reasons.Add("错误 $([int]$Errors) 项（诊断面 $($DiagnosticsScope -replace '^$', '<未声明>')）") }
            if ([int]$Warnings -gt 0 -and $WarningsBlock) { $reasons.Add("警告 $([int]$Warnings) 项（策略：警告阻断）") }
        }
    }

    foreach ($counter in @($RequiredPositiveCounters)) {
        if ([string]::IsNullOrWhiteSpace($counter)) { continue }
        if (-not $Counters.Contains($counter)) {
            $reasons.Add("核心计数缺失 '$counter'（不可判定 ⇒ 红）")
            continue
        }
        if ([int]$Counters[$counter] -le 0) {
            $reasons.Add("核心计数 '$counter' = $([int]$Counters[$counter])（须 > 0 ⇒ 红）")
        }
    }

    if (@($Criteria).Count -eq 0) {
        $reasons.Add('判据输入为空（未提供任何判据 ⇒ 红）')
    }
    foreach ($criterion in @($Criteria)) {
        $name = [string]$criterion.Name
        $state = [string]$criterion.State
        $count = if ($null -ne $criterion.Count) { [int]$criterion.Count } else { 0 }
        $validation = Test-GateCriterionInput -Name $name -State $state -Count $count
        switch ($validation.Verdict) {
            'Accepted' { $accepted.Add($validation.Name) }
            'NotApplicable' { $notApplicable++ }
            default { $reasons.Add($validation.Reason) }
        }
    }
    if ($accepted.Count -eq 0 -and $notApplicable -eq 0) {
        $reasons.Add('无任何可计入判据（空判据面 ⇒ 红）')
    }

    if ($null -eq $Reconciliation) {
        if ($RequireReconciliation) {
            $reasons.Add('未提供多面对账报告（声明面 ↔ 派发面 ↔ 装配面不可判定 ⇒ 红）')
        }
    }
    elseif (-not [bool]$Reconciliation.Ok) {
        $reportReasons = @($Reconciliation.Reasons)
        if ($reportReasons.Count -gt 0) {
            foreach ($reason in $reportReasons) { $reasons.Add([string]$reason) }
        }
        else {
            $reasons.Add("多面对账覆盖不完整（$($Reconciliation.Coverage)）")
        }
    }

    $verdict = if ($reasons.Count -gt 0) { 'Red' } elseif ($accepted.Count -gt 0) { 'Green' } else { 'NotApplicable' }
    return [pscustomobject]@{
        Verdict          = $verdict
        Reasons          = @($reasons)
        AcceptedCriteria = @($accepted)
        IsGreen          = ($verdict -eq 'Green')
    }
}

<#
.SYNOPSIS
    对账 / 健康判据的正负对照（Good 必绿 / Bad 必红），直接喂给 Assert-GateControls。
#>
function New-GateFaceControlCases {
    [CmdletBinding()]
    param()

    $healthy = [pscustomobject]@{
        Ok       = $true
        Coverage = 'complete'
        Reasons  = @()
    }
    $empty = [pscustomobject]@{
        Ok       = $false
        Coverage = 'empty'
        Reasons  = @('面为空：declared（空面 = 无标的，不是「无需对账」）')
    }

    $predicate = {
        param($sample)
        $verdict = Get-GateHealthVerdict `
            -Criteria $sample.Criteria `
            -Errors $sample.Errors `
            -Warnings $sample.Warnings `
            -Counters $sample.Counters `
            -RequiredPositiveCounters $sample.RequiredPositiveCounters `
            -Reconciliation $sample.Reconciliation `
            -RequireReconciliation $sample.RequireReconciliation `
            -DiagnosticsAggregated $sample.DiagnosticsAggregated
        return (-not $verdict.IsGreen)
    }

    $good = @{
        Criteria                = @(@{ Name = 'attached'; State = 'Loaded'; Count = 2 })
        Errors                  = 0
        Warnings                = 0
        Counters                = @{ attached = 2 }
        RequiredPositiveCounters = @('attached')
        Reconciliation          = $healthy
        RequireReconciliation   = $true
        DiagnosticsAggregated   = $true
    }

    return @(
        @{
            Id = 'face-health-good'; Predicate = $predicate; Good = $good
            Bad = @{
                Criteria = @(@{ Name = 'attached'; State = 'Loaded'; Count = 2 }); Errors = 0; Warnings = 0
                Counters = @{ attached = 0 }; RequiredPositiveCounters = @('attached')
                Reconciliation = $healthy; RequireReconciliation = $true; DiagnosticsAggregated = $true
            }
        },
        @{
            Id = 'face-health-empty-criteria'; Predicate = $predicate; Good = $good
            Bad = @{
                Criteria = @(); Errors = 0; Warnings = 0
                Counters = @{ attached = 2 }; RequiredPositiveCounters = @('attached')
                Reconciliation = $healthy; RequireReconciliation = $true; DiagnosticsAggregated = $true
            }
        },
        @{
            Id = 'face-health-diagnostics-unaggregated'; Predicate = $predicate; Good = $good
            Bad = @{
                Criteria = @(@{ Name = 'attached'; State = 'Loaded'; Count = 2 }); Errors = 0; Warnings = 0
                Counters = @{ attached = 2 }; RequiredPositiveCounters = @('attached')
                Reconciliation = $healthy; RequireReconciliation = $true; DiagnosticsAggregated = $false
            }
        },
        @{
            Id = 'face-health-reconciled-empty'; Predicate = $predicate; Good = $good
            Bad = @{
                Criteria = @(@{ Name = 'attached'; State = 'Loaded'; Count = 2 }); Errors = 0; Warnings = 0
                Counters = @{ attached = 2 }; RequiredPositiveCounters = @('attached')
                Reconciliation = $empty; RequireReconciliation = $true; DiagnosticsAggregated = $true
            }
        },
        @{
            Id = 'face-health-warnings-block'; Predicate = $predicate; Good = $good
            Bad = @{
                Criteria = @(@{ Name = 'attached'; State = 'Loaded'; Count = 2 }); Errors = 0; Warnings = 3
                Counters = @{ attached = 2 }; RequiredPositiveCounters = @('attached')
                Reconciliation = $healthy; RequireReconciliation = $true; DiagnosticsAggregated = $true
            }
        }
    )
}

<#
.SYNOPSIS
    执行对账 / 健康判据自证（缺 Good 或 Bad 即违规）。
#>
function Assert-GateFaceControls {
    [CmdletBinding()]
    param(
        [string]$Label = '对账/健康判据自证',
        [System.Collections.Generic.List[string]]$Violations = $null
    )
    return Assert-GateControls -Cases (New-GateFaceControlCases) -Label $Label -Violations $Violations
}

Export-ModuleMember -Function Get-GateFaceReconciliation, Test-GateCriterionInput, Get-GateHealthVerdict, New-GateFaceControlCases, Assert-GateFaceControls
