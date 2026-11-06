<#
.SYNOPSIS
    通用启动执行体（产品无关）—— 契约驱动 + S0–S7 阶段表 + LaunchTrace。

.DESCRIPTION
    唯一实现：判定（Test-BuildGate）→ 按需构建（Build-Effective）→ 前置断言（装配闭包/二进制契约）
    → 生命周期（ProcessStartInfo + WaitForExit，唯一所有者）→ 收尾（窗口保持/退出码映射）。
    产品差异全部来自契约：Share\Launch\Launchers\<product>.launch.psd1。本脚本禁出现任何产品路径拼接。

    形参只有三件：-Product（契约标识）/ -Entry（入口 id，缺省取契约 DefaultEntry）/ -RawArgs（原样命令行）。
    任何失败都必须留下 trace（Data\Tests\launch\<product>\<ts>.log），可由日志独立复算根因。

    退出码（契约 ExitCodes）：0=ok / 2=precondition / 3=closure / 4=build / 5=busy / 6=smoke
    0 只由 S6 的进程结构事实（ExitCode）产生；命令模式（gate-only/diagnose/build-only）用完成码常量，不产生常量 0。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Product,
    [string]$Entry = '',
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$RawArgs = @()
)

$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'LaunchContract.psm1') -Force -DisableNameChecking

# ---- 仓库根 / 契约装载（失败也要可见：先落最小 trace）----
$repoRoot = Get-LaunchRepoRoot -StartPath $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($repoRoot)) { Write-Host '[launch] 仓库根定位失败' -ForegroundColor Red; exit 2 }

$contractPath = Get-LaunchContractPath -RepoRoot $repoRoot -Product $Product
if (-not (Test-Path -LiteralPath $contractPath)) {
    Write-Host ("[launch] exit 2：启动契约缺失 {0}" -f $contractPath) -ForegroundColor Red
    Exit-Launch -Code 2
}
$contract = Import-LaunchContract -Path $contractPath

# 入口声明先行解析（S0 需要入口级 Environment delta；S1 再做契约合格性判定）
$entryDef = $null
try { $entryDef = Get-LaunchContractEntry -Contract $contract -EntryId $Entry } catch { }

# ---- LaunchTrace（全程 tee；头部可复算：契约哈希 + 归一 argv + 环境摘要）----
$script:TracePath = $null

function New-LaunchTrace {
    param($Contract, [string]$ContractPath, [string]$RepoRoot)

    $rel = [string]$Contract['Trace']['Dir']
    if ([string]::IsNullOrWhiteSpace($rel)) { $rel = 'Data\Tests\launch' }
    $productDir = Join-Path (Join-Path $RepoRoot $rel) ([string]$Contract['Product'])
    if (-not (Test-Path -LiteralPath $productDir)) { New-Item -ItemType Directory -Path $productDir -Force | Out-Null }

    $keepDays = [int]$Contract['Trace']['KeepDays']
    if ($keepDays -gt 0) {
        $cut = (Get-Date).AddDays(-$keepDays)
        $archive = Join-Path $productDir 'archive'
        foreach ($old in @(Get-ChildItem -LiteralPath $productDir -Filter '*.log' -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt $cut })) {
            if (-not (Test-Path -LiteralPath $archive)) { New-Item -ItemType Directory -Path $archive -Force | Out-Null }
            Move-Item -LiteralPath $old.FullName -Destination $archive -Force -ErrorAction SilentlyContinue
        }
    }

    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $path = Join-Path $productDir ($stamp + '.log')
    $n = 1
    while (Test-Path -LiteralPath $path) {
        $path = Join-Path $productDir ('{0}_{1}.log' -f $stamp, $n)
        $n++
    }
    return [pscustomobject]@{ Dir = $productDir; Path = $path }
}

function Write-LaunchTrace {
    param([string]$Level = 'INFO', [string]$Message = '')
    if ([string]::IsNullOrWhiteSpace($script:TracePath)) { return }
    $line = ('[{0}] [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff'), $Level, $Message)
    Add-Content -LiteralPath $script:TracePath -Value $line -Encoding UTF8
}

function Write-LaunchConsole {
    param([string]$Message = '', [string]$Color = 'Gray')
    Write-Host $Message -ForegroundColor $Color
}

# 失败/命令模式统一出口：先按契约 Window.HoldOnFailure 保持窗口，再退出。
# 判据载体 = 退出码（结构事实）；禁把「失败停窗」留给 bat（bat 已零逻辑）。
$script:NoPause = (@($RawArgs) -contains '--no-pause')
function Exit-Launch {
    param([int]$Code)
    Write-LaunchTrace 'STEP' ("[S7] 收尾：code={0} hold={1}" -f $Code, ($Code -ne 0 -and -not $script:NoPause))
    if ($Code -ne 0 -and -not $script:NoPause) {
        Write-LaunchConsole '' 'Gray'
        Write-LaunchConsole ("[launch] 失败（exit {0}）。按任意键关闭窗口…" -f $Code) 'Red'
        try { [void][Console]::ReadKey($true) } catch { }
    }
    exit $Code
}

$trace = New-LaunchTrace -Contract $contract -ContractPath $contractPath -RepoRoot $repoRoot
$script:TracePath = $trace.Path
$contractHash = Get-LaunchContractHash -Path $contractPath

Write-LaunchConsole ''
Write-LaunchConsole '============================================================' 'Cyan'
Write-LaunchConsole ("  启动（通用执行体）产品={0}" -f $Product) 'Cyan'
Write-LaunchConsole '============================================================' 'Cyan'
Write-LaunchConsole ("  trace: {0}" -f $trace.Path) 'DarkGray'

# ---- S0 归一环境（契约 Environment，空值才写；控制台编码统一，替代 bat 的 chcp）----
Write-LaunchTrace 'STEP' '[S0] 归一环境：开始'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { Write-LaunchTrace 'WARN' ('[S0] 控制台编码设置失败：' + $_.Exception.Message) }
try { $OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$appliedEnv = [System.Collections.Generic.List[string]]::new()
foreach ($name in @($contract['Environment'].Keys)) {
    $value = [string]$contract['Environment'][$name]
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        Set-Item -Path ('Env:{0}' -f $name) -Value $value
        $appliedEnv.Add(('{0}={1}' -f $name, $value))
    }
}
if ($null -ne $entryDef) {
    foreach ($name in @($entryDef.Env.Keys)) {
        $value = [string]$entryDef.Env[$name]
        Set-Item -Path ('Env:{0}' -f $name) -Value $value
        $appliedEnv.Add(('{0}={1}(entry:{2})' -f $name, $value, $entryDef.Id))
    }
}
Write-LaunchTrace 'STEP' ("[S0] 归一环境：ok applied={0}" -f $appliedEnv.Count)

# ---- S1 契约校验（Schema / 字段完备 / 入口双向映射）----
$violations = Test-LaunchContract -Contract $contract -ContractPath $contractPath -RepoRoot $repoRoot
if (@($violations).Count -gt 0) {
    Write-LaunchTrace 'ERROR' ("[S1] 契约校验：fail count={0}" -f @($violations).Count)
    foreach ($x in @($violations)) { Write-LaunchConsole ("  [契约] {0}" -f $x) 'Red'; Write-LaunchTrace 'ERROR' ('  ' + $x) }
    Write-LaunchConsole '[launch] exit 2：启动契约不合格（见上）' 'Red'
    Exit-Launch -Code 2
}
$entryDef = Get-LaunchContractEntry -Contract $contract -EntryId $Entry
if ([string]::IsNullOrWhiteSpace($Entry)) { $Entry = $entryDef.Id }
Write-LaunchConsole ("  入口: {0}（{1}）" -f $entryDef.Id, $entryDef.Script) 'Gray'
Write-LaunchTrace 'STEP' ("[S1] 契约校验：ok entry={0} script={1} sha256={2}" -f $entryDef.Id, $entryDef.Script, $contractHash)

Write-LaunchTrace 'HEADER' ("trace_version=1")
Write-LaunchTrace 'HEADER' ("contract={0}" -f $contractPath)
Write-LaunchTrace 'HEADER' ("contract_sha256={0}" -f $contractHash)
Write-LaunchTrace 'HEADER' ("product={0} entry={1}" -f $contract['Product'], $entryDef.Id)
Write-LaunchTrace 'HEADER' ("user={0} machine={1}" -f [Environment]::UserName, [Environment]::MachineName)
Write-LaunchTrace 'HEADER' ("env_applied={0}" -f ($appliedEnv -join '; '))

# ---- S2 参数解析（白名单 → 框架开关；Passthrough 前缀 → 转交；其余 ⇒ 退出 2，禁静默透传）----
$options = @{
    NoBuild           = $false
    ForceBuild        = $false
    BuildOnly         = $false
    GateOnly          = $false
    Diagnose          = $false
    KillHost          = $false
    KillAnyPath       = $false
    Smoke             = $false
    NoPause           = $false
    Interactive       = [bool]$entryDef.Interactive
    ContractPreflight = [string]$contract['ContractPreflight']['Default']
    Log               = ''
    Configuration     = [string]$contract['Build']['Configuration']
}

$allArgs = @(@($entryDef.Args) + @($RawArgs) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | ForEach-Object { [string]$_ })
$forward = [System.Collections.Generic.List[string]]::new()
$unknown = [System.Collections.Generic.List[string]]::new()
$paramDefs = $contract['Parameters']
$passthrough = @($contract['Passthrough'])

$i = 0
while ($i -lt $allArgs.Count) {
    $token = $allArgs[$i]
    $name = $token
    $inlineValue = ''
    $hasInline = $false
    if ($token -like '--*=*') {
        $eq = $token.IndexOf('=')
        $name = $token.Substring(0, $eq)
        $inlineValue = $token.Substring($eq + 1)
        $hasInline = $true
    }

    if ($paramDefs.ContainsKey($name)) {
        $kind = [string]$paramDefs[$name]
        if ($kind -eq 'flag') {
            if ($hasInline) { Write-LaunchTrace 'WARN' ("[S2] 开关不接受值（已忽略）：{0}" -f $token) }
            switch ($name) {
                '--no-build'     { $options.NoBuild = $true }
                '--fast'         { $options.ForceBuild = $true }
                '--full'         { $options.ForceBuild = $true }
                '--build-only'   { $options.BuildOnly = $true }
                '--gate-only'    { $options.GateOnly = $true }
                '--diagnose'     { $options.Diagnose = $true }
                '--kill-host'    { $options.KillHost = $true }
                '--any-instance' { $options.KillHost = $true; $options.KillAnyPath = $true }
                '--smoke'        { $options.Smoke = $true }
                '--no-pause'     { $options.NoPause = $true }
                '--interactive'  { $options.Interactive = $true }
            }
        }
        else {
            $value = $inlineValue
            if (-not $hasInline) {
                if ($i + 1 -lt $allArgs.Count -and -not ([string]$allArgs[$i + 1]).StartsWith('--')) {
                    $value = [string]$allArgs[$i + 1]
                    $i++
                }
                else {
                    $unknown.Add(('{0}(缺值)' -f $name))
                }
            }
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                switch ($name) {
                    '--contract-preflight' {
                        if (@($contract['ContractPreflight']['Allowed']) -contains $value) { $options.ContractPreflight = $value }
                        else { $unknown.Add(('{0}={1}(取值非法)' -f $name, $value)) }
                    }
                    '--log' {
                        $options.Log = $value
                        $forward.Add('--log')
                        $forward.Add($value)
                    }
                    '--configuration' { $options.Configuration = $value }
                }
            }
        }
    }
    elseif ($passthrough -contains $name) {
        $forward.Add($token)
        if (-not $hasInline -and $i + 1 -lt $allArgs.Count -and -not ([string]$allArgs[$i + 1]).StartsWith('--')) {
            $forward.Add([string]$allArgs[$i + 1])
            $i++
        }
    }
    else {
        $unknown.Add($token)
    }
    $i++
}

Write-LaunchTrace 'HEADER' ("argv={0}" -f ($allArgs -join ' '))
Write-LaunchTrace 'HEADER' ("forward={0}" -f ($forward -join ' '))

if ($unknown.Count -gt 0) {
    Write-LaunchTrace 'ERROR' ("[S2] 参数解析：fail unknown={0}" -f ($unknown -join ' '))
    Write-LaunchConsole ("  [参数] 不可识别：{0}" -f ($unknown -join ', ')) 'Red'
    Write-LaunchConsole ("  [参数] 框架开关白名单：{0}" -f ((@($paramDefs.Keys) | Sort-Object) -join ' ')) 'Yellow'
    Write-LaunchConsole ("  [参数] 透传前缀：{0}" -f ($passthrough -join ' ')) 'Yellow'
    Write-LaunchConsole '[launch] exit 2：参数不合法（禁静默透传）' 'Red'
    Exit-Launch -Code 2
}
Write-LaunchTrace 'STEP' ("[S2] 参数解析：ok switches=noBuild:{0},force:{1},gate-only:{2},diagnose:{3},build-only:{4},smoke:{5},interactive:{6}" -f `
            [bool]$options.NoBuild, [bool]$options.ForceBuild, [bool]$options.GateOnly, [bool]$options.Diagnose, `
            [bool]$options.BuildOnly, [bool]$options.Smoke, [bool]$options.Interactive)
$script:NoPause = [bool]$options.NoPause

# 命令模式完成码（gate-only / diagnose / build-only）：0 = 命令完成，不是「进程存活」判定（后者仅 S6 产生）
$commandOkCode = 0

# ---- S3 构建判定（唯一判定件 Test-BuildGate，只消费其 JSON）----
$gateScript = Join-Path $repoRoot 'Frame\PowerShellScript\Share\Build\Test-BuildGate.ps1'
if (-not (Test-Path -LiteralPath $gateScript)) {
    Write-LaunchTrace 'ERROR' ("[S3] 判定：fail 缺失判定件 {0}" -f $gateScript)
    Write-LaunchConsole ("[launch] exit 2：构建判定件缺失 {0}" -f $gateScript) 'Red'
    Exit-Launch -Code 2
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$decision = 'Build'
$reason = 'gate-unreadable'
$gateJson = $null
$jsonText = & $gateScript -RepoRoot $repoRoot -Configuration $options.Configuration -Json
try {
    $gateJson = ($jsonText | Out-String) | ConvertFrom-Json
    if ($null -ne $gateJson) {
        $decision = [string]$gateJson.decision
        $reason = [string]$gateJson.reasonCode
    }
}
catch {
    Write-LaunchTrace 'WARN' ("[S3] 判定输出不可解析（按需要构建）：{0}" -f $_.Exception.Message)
}

if ($options.ForceBuild) { $decision = 'Build'; $reason = 'forced' }
if ($options.NoBuild) { $decision = 'Skip'; $reason = 'no-build' }
$sw.Stop()

Write-LaunchTrace 'STEP' ("[S3] 判定：ok decision={0} reason={1} took_ms={2}" -f $decision, $reason, [int]$sw.Elapsed.TotalMilliseconds)
if ($null -ne $gateJson) {
    Write-LaunchTrace 'EVIDENCE' ('[S3] gate=' + ($gateJson | ConvertTo-Json -Depth 6 -Compress))
    foreach ($p in @($gateJson.repos.PSObject.Properties)) {
        Write-LaunchConsole ("  判定 {0,-10} head={1} worktree={2} changed={3}" -f $p.Name, [string]$p.Value.head, [string]$p.Value.worktreeId, @($p.Value.changed).Count) 'DarkGray'
    }
    foreach ($m in @($gateJson.artifactsMissing)) {
        if ([string]::IsNullOrWhiteSpace([string]$m)) { continue }
        Write-LaunchConsole ("  缺产物: {0}" -f [string]$m) 'DarkRed'
    }
}

$decisionColor = if ($decision -eq 'Skip') { 'Green' } else { 'Yellow' }
Write-LaunchConsole ("  构建判定：{0}（{1}）" -f $decision, $reason) $decisionColor
if ($reason -like 'worktree-dirty:*') {
    Write-LaunchConsole '  工作区有构建相关改动 ⇒ 按策略不自动编译；显式构建用 --fast / --full' 'DarkGray'
}

# 命令模式：只出证据，不构建、不启动
if ($options.GateOnly -or $options.Diagnose) {
    if ($options.Diagnose) {
        $probe = Join-Path $repoRoot 'Frame\PowerShellScript\Share\Build\Stop-LauncherLocks.ps1'
        if (Test-Path -LiteralPath $probe) {
            $probeArgs = @{ RepoRoot = $repoRoot; ProcessName = [System.IO.Path]::GetFileNameWithoutExtension([string]$contract['Host']['Entry']); DryRun = $true }
            if ($options.KillAnyPath) { $probeArgs['AnyPath'] = $true }
            & $probe @probeArgs
            Write-LaunchTrace 'STEP' ("[S3] 占用/锁探测：ok dryrun exit={0}" -f $LASTEXITCODE)
        }
        else {
            Write-LaunchTrace 'WARN' ("[S3] 占用/锁探测件缺失 {0}" -f $probe)
        }
    }
    Write-LaunchTrace 'STEP' '[S7] 命令模式：完成（未构建、未启动）'
    Write-LaunchConsole '[launch] 命令模式完成（未构建、未启动）' 'DarkGray'
    exit $commandOkCode
}

# ---- S4 构建（按需；唯一构建路径 = Build-Effective）----
if ($decision -eq 'Skip' -and $reason -eq 'adopted') {
    & $gateScript -RepoRoot $repoRoot -Configuration $options.Configuration -UpdateStamp -Origin adopted | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-LaunchTrace 'STEP' '[S4] 基线登记：ok origin=adopted'
        Write-LaunchConsole '  已登记构建基线（origin=adopted）：此后仅当内容/产物变化才构建。' 'DarkGray'
    }
}

if ($decision -eq 'Build') {
    $processName = [System.IO.Path]::GetFileNameWithoutExtension([string]$contract['Host']['Entry'])
    $hostProcs = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
    if ($hostProcs.Count -gt 0) {
        if ($options.KillHost) {
            $stopScript = Join-Path $repoRoot 'Frame\PowerShellScript\Share\Build\Stop-LauncherLocks.ps1'
            $stopArgs = @{ RepoRoot = $repoRoot; ProcessName = $processName }
            if ($options.KillAnyPath) { $stopArgs['AnyPath'] = $true }
            & $stopScript @stopArgs
            if ($LASTEXITCODE -ne 0) {
                Write-LaunchTrace 'ERROR' ("[S4] 占用未解除：fail count={0}" -f $hostProcs.Count)
                Write-LaunchConsole '[launch] exit 5：宿主占用未解除（见上）' 'Red'
                Exit-Launch -Code 5
            }
            Write-LaunchTrace 'STEP' ("[S4] 占用解除：ok count={0}" -f $hostProcs.Count)
        }
        else {
            Write-LaunchTrace 'ERROR' ("[S4] 占用：fail count={0} process={1}" -f $hostProcs.Count, $processName)
            Write-LaunchConsole ("[launch] exit 5：宿主正在运行（{0} 个 {1}），构建会写被锁定的共享面。" -f $hostProcs.Count, $processName) 'Red'
            Write-LaunchConsole '         处置：关闭 UI 后重试，或加 --kill-host 自动结束。' 'Yellow'
            Exit-Launch -Code 5
        }
    }

    $buildEntry = Join-Path $repoRoot ([string]$contract['Build']['Entry'])
    Write-LaunchConsole ("  构建中（{0} / mode={1}）..." -f [System.IO.Path]::GetFileName($buildEntry), [string]$contract['Build']['Mode']) 'Cyan'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    & $buildEntry -Configuration $options.Configuration -Mode ([string]$contract['Build']['Mode'])
    $buildRc = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    $sw.Stop()
    Write-LaunchTrace 'STEP' ("[S4] 构建：result={0} exit={1} took_ms={2}" -f $(if ($buildRc -eq 0) { 'ok' } else { 'fail' }), $buildRc, [int]$sw.Elapsed.TotalMilliseconds)

    if ($buildRc -eq 3) {
        Write-LaunchConsole '[launch] exit 5：共享面被占用（宿主锁定），本次未生效。' 'Red'
        Exit-Launch -Code 5
    }
    if ($buildRc -ne 0) {
        Write-LaunchConsole ("[launch] exit 4：构建失败（exit {0}，见上方输出）" -f $buildRc) 'Red'
        Exit-Launch -Code 4
    }

    & $gateScript -RepoRoot $repoRoot -Configuration $options.Configuration -UpdateStamp | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-LaunchTrace 'WARN' '[S4] 账本写入失败（下次启动会重新判定为需要构建）'
        Write-LaunchConsole '  警告：构建账本未记录 ⇒ 下次启动会重新判定为需要构建' 'Yellow'
    }
}
else {
    Write-LaunchTrace 'STEP' '[S4] 构建：skip（判定=新鲜）'
    Write-LaunchConsole '  跳过构建（判定=新鲜）。' 'Green'
}

if ($options.BuildOnly) {
    Write-LaunchTrace 'STEP' '[S7] 仅构建模式：完成（未启动）'
    Write-LaunchConsole '[launch] 仅构建模式：已完成，未启动。' 'DarkGray'
    exit $commandOkCode
}

# ---- S5 前置断言（契约 Preconditions 顺序执行）----
foreach ($precondition in @($contract['Preconditions'])) {
    switch ([string]$precondition) {
        'none' { Write-LaunchTrace 'STEP' '[S5] 前置：none（跳过）' }
        'assembly-closure' {
            # 门禁落点单源 = GateRuntime.Find-GateScript（委托 directory-map.json 的 gateDomains）；
            # 禁在本脚本内联硬编码 DSL/Gate 物理路径——门禁搬迁后启动面必须自动跟随。
            $gateRuntime = Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'
            $closureGate = ''
            if (Test-Path -LiteralPath $gateRuntime) {
                Import-Module $gateRuntime -Force -DisableNameChecking
                try { $closureGate = [string](Find-GateScript -Name 'AssemblyClosure' -Domain 'Meta') } catch { $closureGate = '' }
            }
            else {
                Write-LaunchTrace 'WARN' ('[S5] 装配闭包：门禁运行时缺失 ' + $gateRuntime)
            }
            Write-LaunchTrace 'EVIDENCE' ('[S5] 装配闭包：gate=' + $closureGate)
            if ([string]::IsNullOrWhiteSpace($closureGate) -or -not (Test-Path -LiteralPath $closureGate)) {
                Write-LaunchTrace 'ERROR' ('[S5] 装配闭包：fail 缺失断言件 ' + $closureGate + '（解析面 AssemblyClosure@Meta）')
                Write-LaunchConsole '[launch] exit 2：装配闭包断言件缺失（解析面 AssemblyClosure@Meta 不可得）' 'Red'
                Exit-Launch -Code 2
            }
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            # fail-closed：先置哨兵 -1，门禁「未 exit 就崩溃」不得被读成通过（0）
            $LASTEXITCODE = -1
            try { & $closureGate -Check }
            catch {
                $sw.Stop()
                Write-LaunchTrace 'ERROR' ('[S5] 装配闭包：异常 ' + $_.Exception.Message)
                Write-LaunchConsole ('[launch] exit 3：装配闭包判定不可得（' + $_.Exception.Message + '）') 'Red'
                Exit-Launch -Code 3
            }
            $closureRc = if ($null -eq $LASTEXITCODE) { -1 } else { $LASTEXITCODE }
            $sw.Stop()
            Write-LaunchTrace 'STEP' ("[S5] 装配闭包：result={0} exit={1} took_ms={2}" -f $(if ($closureRc -eq 0) { 'ok' } else { 'fail' }), $closureRc, [int]$sw.Elapsed.TotalMilliseconds)
            if ($closureRc -ne 0) {
                Write-LaunchConsole '[launch] exit 3：装配闭包不完整（见上方缺件清单）' 'Red'
                Exit-Launch -Code 3
            }
        }
        'binary-contract' {
            $preflightScript = Join-Path $repoRoot ([string]$contract['ContractPreflight']['Script'])
            if (-not (Test-Path -LiteralPath $preflightScript)) {
                Write-LaunchTrace 'ERROR' ('[S5] 二进制契约：fail 缺失断言件 ' + $preflightScript)
                Write-LaunchConsole ('[launch] exit 2：二进制契约断言件缺失 ' + $preflightScript) 'Red'
                Exit-Launch -Code 2
            }
            & $preflightScript -Mode $options.ContractPreflight
            $preflightRc = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
            Write-LaunchTrace 'STEP' ("[S5] 二进制契约：result={0} exit={1} mode={2}" -f $(if ($preflightRc -eq 0) { 'ok' } else { 'fail' }), $preflightRc, $options.ContractPreflight)
            if ($preflightRc -ne 0) {
                Write-LaunchConsole '[launch] exit 3：二进制契约断裂（产物间符号不可解析，直接启动会 TypeLoadException）' 'Red'
                Exit-Launch -Code 3
            }
        }
        default {
            Write-LaunchTrace 'ERROR' ("[S5] 前置：fail 未知项 {0}" -f [string]$precondition)
            Write-LaunchConsole ("[launch] exit 2：未知前置 {0}" -f [string]$precondition) 'Red'
            Exit-Launch -Code 2
        }
    }
}

if ($options.ContractPreflight -eq 'off') {
    Write-LaunchTrace 'STEP' '[S5] 二进制契约前置=off（未做符号闭包断言；启用: --contract-preflight=load）'
    Write-LaunchConsole '  二进制契约前置=off（启用: --contract-preflight=load）' 'DarkGray'
}

# ---- S5b 冒烟（显式开启；默认不在每次启动付 90s）----
if ($options.Smoke) {
    $smokeScript = Join-Path $repoRoot ([string]$contract['Smoke']['Script'])
    if (-not (Test-Path -LiteralPath $smokeScript)) {
        Write-LaunchTrace 'ERROR' ('[S5] 冒烟：fail 缺失实现 ' + $smokeScript)
        Write-LaunchConsole ('[launch] exit 2：冒烟实现缺失 ' + $smokeScript) 'Red'
        Exit-Launch -Code 2
    }
    & $smokeScript -Product ([string]$contract['Product'])
    $smokeRc = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
    Write-LaunchTrace 'STEP' ("[S5] 冒烟：result={0} exit={1}" -f $(if ($smokeRc -eq 0) { 'ok' } else { 'fail' }), $smokeRc)
    if ($smokeRc -ne 0) {
        Write-LaunchConsole '[launch] exit 6：启动冒烟未通过（未越过 bootstrap）' 'Red'
        Exit-Launch -Code 6
    }
}

# ---- S6 启动（ProcessStartInfo + WaitForExit = 生命周期唯一所有者）----
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Share\AssemblyClosure\AssemblyClosure.psm1') -Force -DisableNameChecking
$groupStates = @(Get-ClosureStates -RepoRoot $repoRoot -Configuration $options.Configuration |
    Where-Object { $_.Id -eq [string]$contract['Host']['Group'] })
if ($groupStates.Count -eq 0) {
    Write-LaunchTrace 'ERROR' ("[S6] 装载面：fail 装配闭包未声明组 '{0}'" -f [string]$contract['Host']['Group'])
    Write-LaunchConsole ("[launch] exit 3：装配闭包未声明组 '{0}'" -f [string]$contract['Host']['Group']) 'Red'
    Exit-Launch -Code 3
}

$argumentList = [System.Collections.Generic.List[string]]::new()
if ($options.Interactive) {
    $runner = Join-Path $repoRoot ([string]$contract['Host']['Interactive']['Script'])
    if (-not (Test-Path -LiteralPath $runner)) {
        Write-LaunchTrace 'ERROR' ('[S6] 交互运行器：fail 缺失 ' + $runner)
        Write-LaunchConsole ('[launch] exit 2：交互运行器缺失 ' + $runner) 'Red'
        Exit-Launch -Code 2
    }
    $target = Join-Path $PSHOME 'pwsh.exe'
    if (-not (Test-Path -LiteralPath $target)) { $target = 'pwsh.exe' }
    foreach ($a in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runner)) { $argumentList.Add($a) }
}
else {
    $target = Join-Path ([string]$groupStates[0].Root) ([string]$contract['Host']['Entry'])
    if (-not (Test-Path -LiteralPath $target)) {
        Write-LaunchTrace 'ERROR' ('[S6] 启动器：fail 缺失 ' + $target)
        Write-LaunchConsole ('[launch] exit 3：启动器缺失 ' + $target) 'Red'
        Exit-Launch -Code 3
    }
}
foreach ($a in $forward) { $argumentList.Add([string]$a) }

$workingRoot = [string]$contract['Host']['WorkingRoot']
$workingDirectory = if ($workingRoot -eq 'RepoRoot' -or [string]::IsNullOrWhiteSpace($workingRoot)) { $repoRoot } else { Join-Path $repoRoot $workingRoot }

Write-LaunchConsole ("  启动：{0}" -f $target) 'Cyan'
Write-LaunchTrace 'STEP' ("[S6] 启动：begin target={0} argv={1}" -f $target, ($argumentList -join ' '))

$psi = [System.Diagnostics.ProcessStartInfo]::new($target)
$psi.WorkingDirectory = $workingDirectory
$psi.UseShellExecute = $false
# 交互宿主独占控制台：重定向会让子进程失去控制台句柄（菜单输出被吞 + Clear-Host 报「句柄无效」）。
# 非交互宿主（Launcher.exe）保持 tee 到 trace（LaunchTrace 基线标记不含 [STDOUT]，交互模式不 tee 不破门禁）。
$teeStreams = -not [bool]$options.Interactive
$psi.RedirectStandardOutput = $teeStreams
$psi.RedirectStandardError = $teeStreams
foreach ($a in $argumentList) { $psi.ArgumentList.Add($a) }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$process = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $process) {
    Write-LaunchTrace 'ERROR' '[S6] 启动：fail 进程创建失败'
    Write-LaunchConsole '[launch] exit 2：启动器进程创建失败' 'Red'
    Exit-Launch -Code 2
}
$stdoutTask = $null
$stderrTask = $null
if ($teeStreams) {
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
}
$process.WaitForExit()
$sw.Stop()

$stdoutText = ''
$stderrText = ''
if ($teeStreams) {
    try { if ($stdoutTask.Wait(5000)) { $stdoutText = [string]$stdoutTask.Result } } catch { }
    try { if ($stderrTask.Wait(5000)) { $stderrText = [string]$stderrTask.Result } } catch { }
}

if (-not [string]::IsNullOrEmpty($stdoutText)) {
    Write-LaunchTrace 'STDOUT' '<raw-begin>'
    foreach ($line in ($stdoutText -split "`r?`n")) { Write-LaunchTrace 'STDOUT' $line }
    Write-LaunchTrace 'STDOUT' '<raw-end>'
}
if (-not [string]::IsNullOrEmpty($stderrText)) {
    Write-LaunchTrace 'STDERR' '<raw-begin>'
    foreach ($line in ($stderrText -split "`r?`n")) { Write-LaunchTrace 'STDERR' $line }
    Write-LaunchTrace 'STDERR' '<raw-end>'
}

$processExitCode = $process.ExitCode
Write-LaunchTrace 'STEP' ("[S6] 启动：end pid={0} exit={1} alive_ms={2} tee={3}" -f $process.Id, $processExitCode, [int]$sw.Elapsed.TotalMilliseconds, $teeStreams)

# ---- S7 收尾（窗口保持策略归执行体，不归 bat）----
$hold = $false
if (-not $options.NoPause) {
    if ($processExitCode -ne 0 -and [string]$contract['Window']['HoldOnFailure'] -eq 'always') { $hold = $true }
    elseif ($processExitCode -eq 0 -and [string]$contract['Window']['HoldOnExit'] -eq 'until-key') { $hold = $true }
}
Write-LaunchTrace 'STEP' ("[S7] 收尾：exit={0} hold={1}" -f $processExitCode, $hold)
if ($hold) {
    Write-LaunchConsole '' 'Gray'
    Write-LaunchConsole '[launch] 按任意键关闭窗口…' 'DarkGray'
    try { [void][Console]::ReadKey($true) } catch { }
}
exit $processExitCode





