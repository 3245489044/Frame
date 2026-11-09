<#
.SYNOPSIS
    Frame\Invoke —— 统一调用入口层（单文件分发器；薄转发，无业务逻辑）
.DESCRIPTION
    M5 收敛：原 19 个逐件薄转发脚本收敛为**单一入口** + **分发表**（entries.psd1）；
    公共机制（目标解析 / 按名传参 / SpecCli 装配 / 退出码归一）下沉基础框架
    `PowerShellScript\Modules\Dispatcher\`。

    只组装参数并按名转发到唯一实现；自身不含 `dotnet test`/`dotnet build`（判据 = VerifySingleShot 守卫①）。
    退出码契约：0 通过 / 1 失败 / 2 异常或依赖缺失（子脚本未显式 exit ⇒ 归一为 2）。

    迁移映射（旧 -> 新；19 个旧入口名 -> -Action 值）：
        Search-Repo          -> search-repo          Test-JsonFiles     -> test-jsonfiles
        Invoke-GateCheck     -> invoke-gatecheck     Invoke-Regression  -> invoke-regression
        Locate-Test          -> locate-test          Locate-Build       -> locate-build
        Locate-Port          -> locate-port          Invoke-Verify      -> invoke-verify
        Invoke-BuildEffective-> invoke-buildeffective Invoke-SpecGen    -> invoke-specgen
        Get-SpecChain        -> get-spec-chain       New-SpecChain      -> new-spec-chain
        Use-SpecChain        -> use-spec-chain       Set-SpecChain      -> set-spec-chain
        Remove-SpecChain     -> remove-spec-chain    New-SpecChains     -> new-specchains
        ForEach-SpecCase     -> foreach-speccase     Test-SpecRefs      -> test-specrefs
        Get-SpecGenerated    -> get-specgenerated
    旧 `.\X.ps1 -P v` ⇔ 新 `.\Invoke-Frame.ps1 -Action x -P v`。
    注：invoke-specgen 的 `--manifest` 开关在新入口名为 `-EmitManifest`（与 `-Manifest` 路径参数区分）。
.EXAMPLE
    .\Invoke-Frame.ps1 -Action invoke-verify -Changed 'Frame\CsharpPrj\Share\X.cs'
.EXAMPLE
    .\Invoke-Frame.ps1 -Action locate-port -Key DecisionRecorder.AuditProbe
.EXAMPLE
    .\Invoke-Frame.ps1 -Action get-spec-chain -Explain
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet(
        'search-repo', 'test-jsonfiles', 'invoke-gatecheck',
        'keyspace-check', 'keyspace-verify',
        'invoke-verify', 'invoke-regression', 'locate-test', 'locate-build', 'locate-port', 'locate-failure',
        'invoke-buildeffective', 'launch', 'invoke-launch-selftest', 'launch-smoke', 'llm-gate',
        'invoke-specgen', 'get-spec-chain', 'new-spec-chain', 'use-spec-chain', 'set-spec-chain',
        'remove-spec-chain', 'new-specchains', 'foreach-speccase', 'test-specrefs', 'get-specgenerated',
        'read-agentdb', 'watch-observe', 'state-root-audit', 'task-consistency-audit',
        'graph-doctor', 'graph-archive-legacy', 'graph-key-normalize', 'graph-rebuild'
    )]
    [string]$Action,

    [string[]]$Changed = @(),
    [string]$Plan = '',
    [string]$TestSet = '',
    [string]$GrantFile = '',

    [ValidateSet('default', 'agent', 'infra')]
    [string]$Matrix = 'default',
    [string[]]$Test = @(),
    [string[]]$Project = @(),
    [string[]]$Case = @(),
    [int]$Parallel = 4,
    [switch]$NoKnownRedOk,
    [switch]$WithGates,

    [string[]]$Name = @(),
    [string[]]$Key = @(),
    [string]$Configuration = 'Debug',

    # 启动面透传通道（唯一）：bat 的 %* 原样进入 RawArgs，禁再经环境变量中转
    [string]$Product = '',
    [string]$EntryId = '',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RawArgs = @(),

    [string]$Manifest = '',
    [switch]$EmitManifest,
    [string[]]$Only = @(),
    [string]$Domain = '',
    # CLI 逗号归一（P4）：`pwsh -File` 不会把 "A,B,C" 拆成数组 ⇒ 显式 CSV 变体。
    [string]$OnlyCsv = '',
    [string]$ProjectCsv = '',
    [string]$TestCsv = '',
    [string]$CaseCsv = '',
    [string]$ChangedCsv = '',
    [ValidateSet('fast', 'full')]
    [string]$Tier = 'full',

    [string]$CasesRoot = '',
    [string]$Out = '',
    [string]$OutFile = '',
    [int]$MinShare = 2,
    [int]$MinUses = 2,
    [string]$Suite = '',
    [string]$Chain = '',
    [string]$From = '',
    [switch]$Json,
    [string]$JsonPayload = '',
    [string]$Ports = '',
    [string[]]$Source = @(),
    [string]$Config = '',
    [int]$Port = 0,
    [string]$Listen = '',
    [string]$Upstream = '',
    [string]$DataRoot = '',
    [string]$Pricing = '',
    [ValidateSet('dev', 'locked', 'enforce', 'manual', 'off')]
    [string]$Mode = 'dev',
    [string[]]$Path = @(),
    [string]$Filter = '*.json',
    [string]$Exclude = '',
    [string]$Schema = '',
    [string[]]$Repo = @(),
    [string[]]$PathSpec = @(),
    [string[]]$Pattern = @(),
    [int]$MaxFiles = 200,
    [string]$Shape = '',
    [string]$SuitePattern = '',
    [string]$CasePattern = '',

    [ValidateSet('agent', 'graph', 'all')]
    [string]$Store = 'all',
    [string]$Table = '',
    [string]$Collection = '',
    [string]$Where = '',
    [int]$Limit = 50,
    [string]$StateRoot = '',
    [string]$DllDir = '',
    [string]$TaskKey = '',

    [string]$PipeName = '',
    [string]$PipePrefix = 'agent-observe',
    [switch]$Probe,
    [string]$ScopeId = '',
    [string]$Kind = '',
    [int]$Backlog = 0,
    [switch]$Query,
    [int]$TimeoutMs = 1500,
    [int]$WindowSec = 0,
    [int]$MaxEvents = 0,

    [switch]$All,
    [switch]$Write,
    [switch]$Verify,
    [switch]$Check,
    [switch]$Explain,
    [switch]$ShowDetail,
    [switch]$DryRun,
    [switch]$Assert,
    [switch]$IncludeDeprecated,
    [switch]$Untracked,
    [switch]$IncludeRows,
    [switch]$SelfTest,
    [switch]$Apply,
    [switch]$List,
    [switch]$Literal,
    [switch]$Regex,
    [switch]$FailOnEmpty,
    [switch]$Quiet,

    # ── 调用治理（AX.CallAdmissibility）：票据五要素 + 区分力（缺 ⇒ shadow 记 R1 / enforce 拒绝）──
    [string]$Batch = '',
    [ValidateSet('diagnose', 'edit', 'verify')]
    [string]$Phase = '',
    [string]$Ticket = '',
    [string]$Discriminates = '',
    [string]$Purpose = '',
    [string]$ExpectedOutcome = '',
    [switch]$StrictCalls,
    [switch]$NoLedger
)

$ErrorActionPreference = 'Stop'

# ── 通用机制件（基础框架单一实现点；加载失败即抛，禁静默降级）──
. (Join-Path $PSScriptRoot '..\PowerShellScript\Modules\Dispatcher\_init.ps1')

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$dispatchTable = Import-DispatchTable -Path (Join-Path $PSScriptRoot 'entries.psd1')
$entry = Get-DispatchEntry -Table $dispatchTable -Action $Action
$bound = $PSBoundParameters

# CSV 变体归一（P4）：覆盖同名数组形参后自身移除，避免透传到目标脚本。
$csvMap = @(
    @{ Csv = 'OnlyCsv'; Name = 'Only' },
    @{ Csv = 'ProjectCsv'; Name = 'Project' },
    @{ Csv = 'TestCsv'; Name = 'Test' },
    @{ Csv = 'CaseCsv'; Name = 'Case' },
    @{ Csv = 'ChangedCsv'; Name = 'Changed' }
)
foreach ($m in $csvMap) {
    if ($bound.ContainsKey($m.Csv)) {
        $bound[$m.Name] = @([string]$bound[$m.Csv] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $bound.Remove($m.Csv)
    }
}

# ── 调用治理（CallGovernor）：相位 → 票据/区分力 → 参数契约 → 分账预算 → 零增益重复 ──
# 判据载体 = 结构事实（参数集合/账本计数/声明表），不读执行者叙述；
# shadow 模式只记录并可见化，enforce 模式按原因码拒绝（策略单源 = Baselines\CallGovernance.psd1）。
$envelopeParams = @('Action', 'Batch', 'Phase', 'Ticket', 'Discriminates', 'Purpose', 'ExpectedOutcome', 'StrictCalls', 'NoLedger')
$acceptedParams = @()
if ($entry.ContainsKey('Map')) { $acceptedParams += @($entry['Map'].Keys) }
if ($entry.ContainsKey('Always')) { $acceptedParams += @($entry['Always']) }
if ($entry.ContainsKey('Fixed')) { $acceptedParams += @($entry['Fixed'].Keys) }
$droppedParams = @($bound.Keys | Where-Object { $envelopeParams -notcontains $_ -and $acceptedParams -notcontains $_ })

$governorPath = Join-Path $PSScriptRoot '..\PowerShellScript\Baselines\CallGovernor.psm1'
Import-Module $governorPath -Force -DisableNameChecking
$callPolicy = Import-CallGovernance -RepoRoot $repoRoot
if ($StrictCalls) { $callPolicy['Mode'] = 'enforce' }
$ledger = Open-CallLedger -Policy $callPolicy -RepoRoot $repoRoot -Batch $Batch -Phase $Phase
# 批次/相位经进程环境向子门禁传播（账实对账只读结构事实；不改变任何判定）
[Environment]::SetEnvironmentVariable('WF_CALL_BATCH', $Batch)
[Environment]::SetEnvironmentVariable('WF_CALL_PHASE', $Phase)
$admission = Resolve-CallAdmissibility -Policy $callPolicy -Ledger $ledger -Action $Action -Phase $Phase `
    -DroppedParams $droppedParams -Ticket $Ticket -Discriminates $Discriminates
if ($admission.Hard) {
    Write-Host ("[CallGovernor][{0}] {1}" -f $admission.ReasonCode, $admission.Message) -ForegroundColor Yellow
}
if (-not $admission.Allowed) {
    Get-CallLedgerReport -Ledger $ledger | ForEach-Object { Write-Host $_ }
    if (-not $NoLedger) {
        Write-CallOutcome -Ledger $ledger -Action $Action -ReasonCode $admission.ReasonCode -Discriminates $Discriminates `
            -Phase $Phase -ExitCode 3 -Admitted $false -Note 'rejected' | Out-Null
    }
    exit 3
}

# SpecCli 缺省注入（等价于原 SpecChain 系列 `$PSBoundParameters.ContainsKey('CasesRoot')` 缺省 = AgentTest）
if (-not $bound.ContainsKey('CasesRoot')) {
    $bound['CasesRoot'] = (Join-Path $repoRoot 'Environment\CentralTest\AgentTest')
}
$specCliProject = Join-Path $repoRoot 'Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$specCliDll = Join-Path $repoRoot 'Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

$rc = 0
try {
    switch ([string]$entry['Kind']) {
        'Ps' {
            $target = Resolve-DispatchTarget -BaseDir $PSScriptRoot -RelativePath ([string]$entry['Target'])
            $params = New-DispatchParams -Entry $entry -BoundParameters $bound
            & $target @params
            $rc = Get-NormalizedExitCode
        }
        'SpecCli' {
            if ($Action -eq 'invoke-specgen' -and $EmitManifest) { $bound['Manifest'] = $true }
            $cliArgs = New-SpecCliArgs -Entry $entry -BoundParameters $bound
            if (Test-Path -LiteralPath $specCliDll) {
                & dotnet $specCliDll $cliArgs
            } else {
                & dotnet run --project $specCliProject -c Debug -- $cliArgs
            }
            $rc = Get-NormalizedExitCode
        }
        default {
            throw "[Invoke-Frame] 未知 Kind：$($entry['Kind'])（动作 '$Action'）"
        }
    }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
} finally {
    # 记账（唯一写入口；与退出码同拍，拒绝亦入账已在上面处理）
    if (-not $NoLedger -and $null -ne $ledger) {
        Write-CallOutcome -Ledger $ledger -Action $Action -ReasonCode $admission.ReasonCode `
            -Discriminates $Discriminates -Phase $Phase -ExitCode $rc -Admitted $true -Note 'executed' | Out-Null
    }
}
exit $rc