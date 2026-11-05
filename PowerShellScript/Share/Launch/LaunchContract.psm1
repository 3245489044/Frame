#requires -Version 5.1
<#
.SYNOPSIS
    启动契约（LaunchContract）装载与校验原语 —— 单一实现。

.DESCRIPTION
    契约单源 = Share\Launch\Launchers\<product>.launch.psd1（纯数据）。
    消费方 = Invoke-LaunchEntry.ps1（执行体 S1）/ Invoke-LaunchSelfTest.ps1 / Operation\DSL\Meta\Check-LaunchContract.ps1。
    本件只做「读 + 判 + 派生」，零启动副作用、零产品知识（产品差异全部在 psd1 里）。

    判据载体 = 契约表项 + 文件存在性 + 入口文件集合（结构事实；禁执行者叙述）。
#>

Set-StrictMode -Version Latest

function Get-LaunchRepoRoot {
    <#
    .SYNOPSIS
        由脚本位置向上定位仓库超根（Frame ∧ Projects 同时存在）。
    #>
    [CmdletBinding()]
    param([string]$StartPath = $PSScriptRoot)

    $dir = [System.IO.Path]::GetFullPath($StartPath)
    while (-not [string]::IsNullOrWhiteSpace($dir)) {
        if ((Test-Path -LiteralPath (Join-Path $dir 'Frame')) -and (Test-Path -LiteralPath (Join-Path $dir 'Projects'))) {
            return $dir
        }
        $parent = Split-Path -Parent $dir
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
}

function Get-LaunchContractDir {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    return (Join-Path $RepoRoot 'Frame\PowerShellScript\Share\Launch\Launchers')
}

function Get-LaunchContractPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Product
    )
    return (Join-Path (Get-LaunchContractDir -RepoRoot $RepoRoot) ($Product + '.launch.psd1'))
}

function Get-LaunchContractList {
    <#
    .SYNOPSIS
        枚举全部契约文件（门禁与自测的正对照面）。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $dir = Get-LaunchContractDir -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Filter '*.launch.psd1' -File -ErrorAction SilentlyContinue | Sort-Object Name)
}

function Import-LaunchContract {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Import-PowerShellDataFile -LiteralPath $Path)
}

function Get-LaunchContractHash {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-LaunchIncludedEntries {
    <#
    .SYNOPSIS
        取全部库侧入口（EnableMent\*.bat 文件名集合）。
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $dir = Join-Path $RepoRoot 'EnableMent'
    if (-not (Test-Path -LiteralPath $dir)) { return @() }
    return @(Get-ChildItem -LiteralPath $dir -Filter '*.bat' -File -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Name | Sort-Object)
}

function Get-LaunchContractEntry {
    <#
    .SYNOPSIS
        解析入口声明（id → 批文件 + 附加参数 + 是否交互）。
    .DESCRIPTION
        入口 id 与批文件名解耦：批文件名可为中文/多入口共产品，映射在契约 Entries 内显式声明。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Contract,
        [string]$EntryId = ''
    )

    $entries = $Contract['Entries']
    if ($entries -isnot [System.Collections.IDictionary] -or $entries.Count -eq 0) {
        throw '契约未声明 Entries（入口映射缺失）'
    }

    if ([string]::IsNullOrWhiteSpace($EntryId)) {
        if ($Contract.Contains('DefaultEntry') -and -not [string]::IsNullOrWhiteSpace([string]$Contract['DefaultEntry'])) {
            $EntryId = [string]$Contract['DefaultEntry']
        }
        else {
            $EntryId = [string]@($entries.Keys | Sort-Object)[0]
        }
    }

    if (-not $entries.Contains($EntryId)) {
        throw ("契约未声明入口 '{0}'（已声明：{1}）" -f $EntryId, (@($entries.Keys) -join ', '))
    }

    $def = $entries[$EntryId]
    $envDelta = $def['Env']
    if ($envDelta -isnot [System.Collections.IDictionary]) { $envDelta = @{} }
    return [pscustomobject]@{
        Id          = $EntryId
        Script      = [string]$def['Script']
        Args        = @($def['Args'])
        Interactive = [bool]$def['Interactive']
        Env         = $envDelta
    }
}

function Test-LaunchContract {
    <#
    .SYNOPSIS
        契约合格性判定（纯函数）。
    .OUTPUTS
        string[]（空 = 合格）。每项形如 'K1 子句：明细'。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Contract,
        [Parameter(Mandatory = $true)][string]$ContractPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $v = [System.Collections.Generic.List[string]]::new()

    if ($Contract -isnot [System.Collections.IDictionary]) {
        $v.Add('K0 契约根：须为哈希表')
        return [string[]]$v.ToArray()
    }

    if ([string]$Contract['Schema'] -ne 'launch-contract-v1') {
        $v.Add(("K0 Schema：须为 'launch-contract-v1'（实际 '{0}'）" -f [string]$Contract['Schema']))
    }

    $product = [string]$Contract['Product']
    if ([string]::IsNullOrWhiteSpace($product)) {
        $v.Add('K0 Product：缺省')
    }
    else {
        $leaf = [System.IO.Path]::GetFileName($ContractPath)
        if ($leaf -ne ($product + '.launch.psd1')) {
            $v.Add(("K0 Product：契约文件名须为 '{0}.launch.psd1'（实际 '{1}'）" -f $product, $leaf))
        }
        if ($product -match '[\\/]') { $v.Add('K0 Product：禁含路径分隔符') }
    }

    if ($Contract['Host'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K1 Host：缺省')
    }
    else {
        $hostDef = $Contract['Host']
        foreach ($key in @('Group', 'Entry', 'WorkingRoot')) {
            if ([string]::IsNullOrWhiteSpace([string]$hostDef[$key])) { $v.Add(("K1 Host.{0}：缺省" -f $key)) }
        }
        if (-not ([string]$hostDef['Entry']).EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
            $v.Add('K1 Host.Entry：须为 .exe（装载面根由装配闭包解析，契约只写文件名）')
        }
        $interactive = $hostDef['Interactive']
        if ($interactive -isnot [System.Collections.IDictionary] -or [string]::IsNullOrWhiteSpace([string]$interactive['Script'])) {
            $v.Add('K1 Host.Interactive.Script：缺省（交互入口须给出运行器脚本）')
        }
        elseif (-not (Test-Path -LiteralPath (Join-Path $RepoRoot ([string]$interactive['Script'])))) {
            $v.Add(("K1 Host.Interactive.Script：文件不在位 '{0}'" -f [string]$interactive['Script']))
        }
    }

    if ($Contract['Build'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K2 Build：缺省')
    }
    else {
        $build = $Contract['Build']
        $buildEntry = [string]$build['Entry']
        if ([string]::IsNullOrWhiteSpace($buildEntry)) { $v.Add('K2 Build.Entry：缺省') }
        elseif (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $buildEntry))) { $v.Add(("K2 Build.Entry：文件不在位 '{0}'" -f $buildEntry)) }
        if (@('dev', 'locked') -notcontains [string]$build['Mode']) {
            $v.Add(("K2 Build.Mode：须 ∈ {{dev,locked}}（实际 '{0}'）" -f [string]$build['Mode']))
        }
    }

    foreach ($p in @($Contract['Preconditions'])) {
        if (@('assembly-closure', 'binary-contract', 'none') -notcontains [string]$p) {
            $v.Add(("K3 Preconditions：未知前置 '{0}'（受控词 assembly-closure/binary-contract/none）" -f [string]$p))
        }
    }

    if ($Contract['Smoke'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K4 Smoke：缺省')
    }
    else {
        $smoke = $Contract['Smoke']
        $smokeScript = [string]$smoke['Script']
        if ([string]::IsNullOrWhiteSpace($smokeScript)) { $v.Add('K4 Smoke.Script：缺省') }
        elseif (-not (Test-Path -LiteralPath (Join-Path $RepoRoot $smokeScript))) { $v.Add(("K4 Smoke.Script：文件不在位 '{0}'" -f $smokeScript)) }
        if ([int]$smoke['TimeoutSeconds'] -le 0) { $v.Add('K4 Smoke.TimeoutSeconds：须 > 0') }
        if ([int]$smoke['AliveSeconds'] -le 0) { $v.Add('K4 Smoke.AliveSeconds：须 > 0') }
    }

    if ($Contract['ExitCodes'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K5 ExitCodes：缺省')
    }
    else {
        $codes = $Contract['ExitCodes']
        foreach ($k in @($codes.Keys)) {
            if ([string]$k -notmatch '^\d+$') { $v.Add(("K5 ExitCodes：键须为十进制码（实际 '{0}'）" -f [string]$k)) }
            if ([string]::IsNullOrWhiteSpace([string]$codes[$k])) { $v.Add(("K5 ExitCodes.{0}：语义缺省" -f [string]$k)) }
        }
        if (-not $codes.Contains(0)) { $v.Add('K5 ExitCodes：须声明 0=ok') }
        if (-not $codes.Contains(2)) { $v.Add('K5 ExitCodes：须声明 2=precondition（失败可归因的最低码）') }
    }

    if ($Contract['Parameters'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K6 Parameters：缺省（框架开关白名单不可缺）')
    }
    else {
        foreach ($k in @($Contract['Parameters'].Keys)) {
            if ([string]$k -notmatch '^--[A-Za-z0-9\-]+$') { $v.Add(("K6 Parameters：键须为 '--x' 形态（实际 '{0}'）" -f [string]$k)) }
            if (@('flag', 'value') -notcontains [string]$Contract['Parameters'][$k]) {
                $v.Add(("K6 Parameters.{0}：类型须 ∈ {{flag,value}}（实际 '{1}'）" -f [string]$k, [string]$Contract['Parameters'][$k]))
            }
        }
    }

    if ($Contract['Passthrough'] -isnot [System.Collections.IEnumerable] -or $Contract['Passthrough'] -is [string]) {
        $v.Add('K6 Passthrough：须为数组')
    }
    else {
        foreach ($p in @($Contract['Passthrough'])) {
            if ([string]$p -notmatch '^--[A-Za-z0-9\-]+$') { $v.Add(("K6 Passthrough：项须为 '--x' 形态（实际 '{0}'）" -f [string]$p)) }
        }
    }

    $entryDir = Join-Path $RepoRoot 'EnableMent'
    if ($Contract['Entries'] -isnot [System.Collections.IDictionary] -or $Contract['Entries'].Count -eq 0) {
        $v.Add('K7 Entries：缺省（入口映射不可缺）')
    }
    else {
        $mapped = @{}
        foreach ($id in @($Contract['Entries'].Keys)) {
            $script = [string]$Contract['Entries'][$id]['Script']
            if ([string]::IsNullOrWhiteSpace($script)) { $v.Add(("K7 Entries.{0}.Script：缺省" -f $id)); continue }
            if (-not (Test-Path -LiteralPath (Join-Path $entryDir $script))) {
                $v.Add(("K7 Entries.{0}.Script：入口文件不在位 '{1}'" -f $id, $script))
            }
            if ($mapped.ContainsKey($script)) {
                $v.Add(("K7 Entries：入口 '{0}' 被多个契约项映射（{1} / {2}）⇒ 绑定不确定" -f $script, $mapped[$script], $id))
            }
            else { $mapped[$script] = $id }
        }
        foreach ($bat in (Get-LaunchIncludedEntries -RepoRoot $RepoRoot)) {
            if (-not $mapped.ContainsKey($bat)) {
                $v.Add(("K7 Entries：入口 '{0}' 无契约映射（新增入口须在契约 Entries 登记）" -f $bat))
            }
        }
    }

    if ($Contract['Environment'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K8 Environment：缺省（须为哈希表，可空表）')
    }
    else {
        foreach ($k in @($Contract['Environment'].Keys)) {
            if ([string]::IsNullOrWhiteSpace([string]$Contract['Environment'][$k])) {
                $v.Add(("K8 Environment.{0}：值缺省（禁注入空值）" -f $k))
            }
        }
    }

    if ($Contract['Window'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K9 Window：缺省')
    }
    else {
        if (@('until-key', 'never') -notcontains [string]$Contract['Window']['HoldOnExit']) {
            $v.Add(("K9 Window.HoldOnExit：须 ∈ {{until-key,never}}（实际 '{0}'）" -f [string]$Contract['Window']['HoldOnExit']))
        }
        if (@('always', 'never') -notcontains [string]$Contract['Window']['HoldOnFailure']) {
            $v.Add(("K9 Window.HoldOnFailure：须 ∈ {{always,never}}（实际 '{0}'）" -f [string]$Contract['Window']['HoldOnFailure']))
        }
    }

    if ($Contract['Trace'] -isnot [System.Collections.IDictionary]) {
        $v.Add('K10 Trace：缺省（失败不可观测）')
    }
    else {
        if ([string]::IsNullOrWhiteSpace([string]$Contract['Trace']['Dir'])) { $v.Add('K10 Trace.Dir：缺省') }
        if ([int]$Contract['Trace']['KeepDays'] -le 0) { $v.Add('K10 Trace.KeepDays：须 > 0') }
        $tee = @($Contract['Trace']['Tee'])
        foreach ($stream in @('stdout', 'stderr')) {
            if ($tee -notcontains $stream) { $v.Add(("K10 Trace.Tee：须含 '{0}'（原文保留，禁摘要）" -f $stream)) }
        }
    }

    if ($Contract['Gates'] -isnot [System.Collections.IEnumerable] -or $Contract['Gates'] -is [string]) {
        $v.Add('K11 Gates：须为数组（可为空数组）')
    }

    $text = [System.IO.File]::ReadAllText($ContractPath)
    if ($text -match 'BuildOutput') {
        $v.Add("K12 契约文本：出现 'BuildOutput' 字面（装载面根须由装配闭包解析，契约只写 Group）")
    }

    return [string[]]$v.ToArray()
}

Export-ModuleMember -Function @(
    'Get-LaunchRepoRoot',
    'Get-LaunchContractDir',
    'Get-LaunchContractPath',
    'Get-LaunchContractList',
    'Import-LaunchContract',
    'Get-LaunchContractHash',
    'Get-LaunchIncludedEntries',
    'Get-LaunchContractEntry',
    'Test-LaunchContract'
)

