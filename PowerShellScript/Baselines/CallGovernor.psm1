<#
.SYNOPSIS
    调用治理原语（CallGovernor）—— 票据 / 契约 / 预算 / 增益 / 相位 / 账本 的唯一实现点。

.DESCRIPTION
    5.1 兼容（硬约束）：本模块不使用 #requires -Version 7.0，也不使用 5.1 不支持的
    ConvertFrom-Json -AsHashtable / -Depth（读取账本 JSON 不需要 -Depth 参数）。
    治「执行者自证」：原调用约束只写在 .clinerules（声明层），无任何运行期执行体，
    判据载体 = 执行者叙述（违 SEP.JudgmentCarrierStructural）。本模块把四类判定搬成宿主代码：

      Import-CallGovernance     读策略声明（单一真源；缺失即抛，禁静默降级）
      Open-CallLedger           开批账（每批一账；总账不豁免分账）
      Resolve-CallAdmissibility 表驱动裁决 → @{Allowed; ReasonCode; Message; Warnings; Ledger}
      Write-CallOutcome         写账（唯一写入口；拒绝也入账）
      Get-CallLedgerReport      已用清单（拒绝时的可见化面）

    纪律：本模块零业务语义（不认识具体工具），只认「动作名 + 账别 + 票据字段」；
    所有原因码来自声明面，禁在代码里写字面量。
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Import-CallGovernance {
    <#
    .SYNOPSIS
        载入调用治理策略声明（唯一真源）。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$Path = ''
    )

    if (-not $Path) { $Path = Join-Path $RepoRoot 'Frame\PowerShellScript\Baselines\CallGovernance.psd1' }
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "[CallGovernor] 策略声明缺失：$Path（禁以缺省值代跑，违 fail-closed）"
    }

    $policy = Import-PowerShellDataFile -LiteralPath $Path
    foreach ($required in @('Schema', 'Mode', 'ReasonCodes', 'Budgets', 'ActionClass', 'PhaseDeny', 'Attribution', 'LedgerDir', 'RequireBatch', 'RequirePhase')) {
        if (-not $policy.ContainsKey($required)) {
            throw "[CallGovernor] 策略声明缺键 '$required'：$Path"
        }
    }

    foreach ($required in @('OK', 'R1', 'R2', 'R3', 'R4', 'R5', 'R6', 'R7', 'R8', 'R9')) {
        if (-not $policy.ReasonCodes.ContainsKey($required)) {
            throw "[CallGovernor] 原因码缺项 '$required'（受控词不闭合）：$Path"
        }
    }

    $policy['PolicyPath'] = $Path
    return $policy
}

function Open-CallLedger {
    <#
    .SYNOPSIS
        开批账（已存在则续用；跨调用累积，达上限即 Must-Stop）。
    #>
    param(
        [Parameter(Mandatory = $true)]$Policy,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$Batch = '',
        [string]$Phase = ''
    )

    $id = if ($Batch) { $Batch } else { 'adhoc-' + (Get-Date -Format 'yyyyMMdd') }
    $dir = Join-Path $RepoRoot $Policy.LedgerDir
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $path = Join-Path $dir ($id + '.json')

    $doc = $null
    if (Test-Path -LiteralPath $path) {
        try { $doc = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { $doc = $null }
    }

    if ($null -eq $doc) {
        $doc = [pscustomobject]@{
            schema  = 'call-ledger-v1'
            batch   = $id
            phase   = $Phase
            seq     = 0
            counts  = [pscustomobject]@{ shell = 0; diagnose = 0; verify = 0; rejected = 0; zeroGain = 0 }
            entries = @()
        }
        # 开批即落盘（批次开始置 0）：账本在位本身是「非裸调」的结构证据（门禁 G8 判据）。
        $doc | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
    }

    return [pscustomobject]@{ Id = $id; Path = $path; Doc = $doc; Policy = $Policy }
}

function Get-CallActionClass {
    <#
    .SYNOPSIS
        动作 → 账别（未登记 ⇒ shell 总账）。
    #>
    param([Parameter(Mandatory = $true)]$Policy, [string]$Action)

    if ($Action -and $Policy.ActionClass.ContainsKey($Action)) { return [string]$Policy.ActionClass[$Action] }
    return 'shell'
}

function Resolve-CallAdmissibility {
    <#
    .SYNOPSIS
        表驱动裁决：相位 → 环境完整性 → 票据/区分力 → 参数契约 → 分账预算 → 零增益重复。
    .OUTPUTS
        @{ Allowed; ReasonCode; Message; Hard; Warnings; Ledger; ActionClass }
    #>
    param(
        [Parameter(Mandatory = $true)]$Policy,
        [Parameter(Mandatory = $true)]$Ledger,
        [Parameter(Mandatory = $true)][string]$Action,
        [string]$Phase = '',
        [string[]]$DroppedParams = @(),
        [string]$Ticket = '',
        [string]$Discriminates = ''
    )

    $warnings = [System.Collections.Generic.List[string]]::new()
    $codes = $Policy.ReasonCodes
    $enforce = ([string]$Policy.Mode -eq 'enforce')

    function New-Verdict([string]$Code, [string]$Message, [bool]$Hard) {
        return [pscustomobject]@{
            Allowed     = -not ($Hard -and $enforce)
            ReasonCode  = $Code
            Message     = $Message
            Hard        = $Hard
            Warnings    = @($warnings)
            Ledger      = $Ledger
            ActionClass = Get-CallActionClass -Policy $Policy -Action $Action
        }
    }

    # R6 相位禁止（登记相位禁列出的动作；未登记相位不施加约束）
    if ($Phase -and $Policy.PhaseDeny.ContainsKey($Phase)) {
        if (@($Policy.PhaseDeny[$Phase]) -contains $Action) {
            return New-Verdict $codes.R6 ("相位 '$Phase' 禁执行动作 '$Action'（相位白名单外）") $true
        }
    }

    # R1 票据 + 区分力（写不出「结果A⇒H1 / 结果B⇒H2」⇒ 不该调用）
    # 例外：LowRiskActions（产品运行时归一面）不承担证据采集职责 ⇒ 免票据/免区分力（判据载体 = 表项）。
    $lowRisk = $Policy.ContainsKey('LowRiskActions') -and (@($Policy.LowRiskActions) -contains $Action)

    # R9 环境完整性（批次/相位 = 调用前凭证；缺失即越权，事后补记不构成许可）
    if (-not $lowRisk) {
        if ($Policy.ContainsKey('RequireBatch') -and [bool]$Policy.RequireBatch) {
            $batchId = [string]$Ledger.Doc.batch
            if ([string]::IsNullOrWhiteSpace($batchId) -or $batchId -like 'adhoc-*') {
                return New-Verdict $codes.R9 '缺批次声明（-Batch；无批次 ⇒ 无法账实对账，禁以 adhoc 代跑）' $true
            }
        }
        if ($Policy.ContainsKey('RequirePhase') -and [bool]$Policy.RequirePhase) {
            if ([string]::IsNullOrWhiteSpace($Phase)) {
                return New-Verdict $codes.R9 '缺相位声明（-Phase diagnose|edit|verify；相位不可逆，事后补记不构成许可）' $true
            }
        }
    }

    if (-not $lowRisk) {
        if ($Policy.RequireTicket -and [string]::IsNullOrWhiteSpace($Ticket)) {
            return New-Verdict $codes.R1 '缺调用票据（须声明 Purpose/ExpectedOutcome/Discriminates/CostClass）' $true
        }
        if ($Policy.RequireDiscrimination -and [string]::IsNullOrWhiteSpace($Discriminates)) {
            return New-Verdict $codes.R1 '缺区分力 Discriminates（结果A⇒H1 / 结果B⇒H2；写不出即不该调用）' $true
        }
    }

    # R2 参数契约（未声明参数被静默丢弃 ⇒ 语义静默改变，如 -Switch value 错位）
    if (@($DroppedParams).Count -gt 0) {
        return New-Verdict $codes.R2 ("未声明参数被丢弃：{0}（禁静默改语义）" -f (@($DroppedParams) -join ', ')) $true
    }

    # R4 分账预算（总账不豁免分账）
    # LowRiskActions 例外：产品运行时归一面不占「证据采集」预算（与 R1 免票口径一致，否则免票是半截的）。
    $class = Get-CallActionClass -Policy $Policy -Action $Action
    if (-not $lowRisk) {
        $shellUsed = [int]$Ledger.Doc.counts.shell
        if ($shellUsed -ge [int]$Policy.Budgets.shell) {
            return New-Verdict $codes.R4 ("本批 shell 调用已达上限 {0}" -f $Policy.Budgets.shell) $true
        }
        if ($class -eq 'verify' -and [int]$Ledger.Doc.counts.verify -ge [int]$Policy.Budgets.verify) {
            return New-Verdict $codes.R4 ("本批验证调用已达上限 {0}" -f $Policy.Budgets.verify) $true
        }
        if ($class -eq 'diagnose' -and [int]$Ledger.Doc.counts.diagnose -ge [int]$Policy.Budgets.diagnose) {
            return New-Verdict $codes.R4 ("本批诊断调用已达上限 {0}" -f $Policy.Budgets.diagnose) $true
        }
    }

    # R5 零增益重复（同区分力 + 同动作 = 命题已判定）
    if (-not $lowRisk -and -not [string]::IsNullOrWhiteSpace($Discriminates)) {
        foreach ($entry in @($Ledger.Doc.entries)) {
            if ($null -eq $entry) { continue }
            if ([string]$entry.discriminates -eq $Discriminates -and [string]$entry.action -eq $Action) {
                return New-Verdict $codes.R5 ("同区分力命题已执行过（seq={0}）；再跑无新增判据" -f $entry.seq) $true
            }
        }
    }

    return [pscustomobject]@{
        Allowed     = $true
        ReasonCode  = $codes.OK
        Message     = 'admitted'
        Hard        = $false
        Warnings    = @($warnings)
        Ledger      = $Ledger
        ActionClass = $class
    }
}

function Write-CallOutcome {
    <#
    .SYNOPSIS
        写账（唯一写入口）：**拒绝也入账**，保证「试过什么」可复算。
    #>
    param(
        [Parameter(Mandatory = $true)]$Ledger,
        [Parameter(Mandatory = $true)][string]$Action,
        [Parameter(Mandatory = $true)][string]$ReasonCode,
        [string]$Discriminates = '',
        [string]$Phase = '',
        [int]$ExitCode = -1,
        [bool]$Admitted = $true,
        [string]$Note = ''
    )

    $doc = $Ledger.Doc
    $doc.seq = [int]$doc.seq + 1

    $class = Get-CallActionClass -Policy $Ledger.Policy -Action $Action
    $doc.counts.shell = [int]$doc.counts.shell + 1
    if ($class -eq 'verify') { $doc.counts.verify = [int]$doc.counts.verify + 1 }
    elseif ($class -eq 'diagnose') { $doc.counts.diagnose = [int]$doc.counts.diagnose + 1 }
    if (-not $Admitted) { $doc.counts.rejected = [int]$doc.counts.rejected + 1 }

    $doc.entries = @($doc.entries) + [pscustomobject]@{
        seq           = [int]$doc.seq
        at            = (Get-Date).ToString('O')
        action        = $Action
        class         = $class
        phase         = $Phase
        admitted      = $Admitted
        reasonCode    = $ReasonCode
        discriminates = $Discriminates
        exitCode      = $ExitCode
        note          = $Note
    }

    $doc | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Ledger.Path -Encoding UTF8
    return $doc
}

function Get-CallLedgerReport {
    <#
    .SYNOPSIS
        已用清单（拒绝/收批时的可见化面：禁只说「超限」而不给已用明细）。
    #>
    param([Parameter(Mandatory = $true)]$Ledger)

    $doc = $Ledger.Doc
    $lines = @(
        ("[CallGovernor] 批次={0} 相位={1} | shell={2}/{3} diagnose={4}/{5} verify={6}/{7} rejected={8}" -f
            $doc.batch, $doc.phase,
            $doc.counts.shell, $Ledger.Policy.Budgets.shell,
            $doc.counts.diagnose, $Ledger.Policy.Budgets.diagnose,
            $doc.counts.verify, $Ledger.Policy.Budgets.verify,
            $doc.counts.rejected)
    )
    foreach ($entry in @($doc.entries)) {
        if ($null -eq $entry) { continue }
        $lines += ("  #{0} {1} [{2}/{3}] {4}" -f $entry.seq, $entry.action, $entry.class, $entry.reasonCode, $entry.discriminates)
    }

    return $lines
}

Export-ModuleMember -Function Import-CallGovernance, Open-CallLedger, Get-CallActionClass,
    Resolve-CallAdmissibility, Write-CallOutcome, Get-CallLedgerReport
