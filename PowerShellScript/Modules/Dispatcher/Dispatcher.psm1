<#
.SYNOPSIS
    入口薄转发的**通用机制件**（单源）—— 目标解析 / 按名传参映射 / SpecCli 参数装配 / 退出码归一。

.DESCRIPTION
    Frame\Invoke 的 19 个入口在收敛前逐字重复同一段样板：
        $target = Join-Path $PSScriptRoot '<rel>'
        $params = @{} ; if ($PSBoundParameters.ContainsKey('X')) { $params['X'] = $X } ...
        $rc = 0
        try { & $target @params; $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE } }
        catch { $rc = 2; Write-Error $_.Exception.Message }
        exit $rc
    本模块把该样板的**可复用机制**提为算子；入口收敛为单一分发器（Frame\Invoke\Invoke-Frame.ps1）
    + 分发表（Frame\Invoke\entries.psd1），逐字语义不变。

    退出码契约（Frame\Invoke\README.md）：0 通过 / 1 失败 / 2 异常或依赖缺失。
    目标未显式 exit 时，$LASTEXITCODE 为 $null ⇒ 归一为 2（与样板一致）。

    依赖方向（硬约束）：CentralTest → Frame（单向）；本件零 CentralTest 引用。
#>

Set-StrictMode -Version Latest

function Get-NormalizedExitCode {
    <#
    .SYNOPSIS
        归一子脚本退出码：未显式 exit（$LASTEXITCODE 为 $null）⇒ 2（异常/依赖缺失）。
    .OUTPUTS
        int（0 / 1 / 2 …）。
    #>
    [CmdletBinding()]
    param()

    if ($null -eq $LASTEXITCODE) { return 2 }
    return [int]$LASTEXITCODE
}

function Resolve-DispatchTarget {
    <#
    .SYNOPSIS
        解析转发目标为绝对路径；不存在即抛（fail-fast，防「转发件指向已删除实现」静默失败）。
    .PARAMETER BaseDir
        相对基准目录（通常 = 入口脚本 $PSScriptRoot）。
    .PARAMETER RelativePath
        目标相对路径。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$BaseDir,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $candidate = Join-Path $BaseDir $RelativePath
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "[Dispatcher] 转发目标不存在：$RelativePath（解析为 $candidate）"
    }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function New-DispatchParams {
    <#
    .SYNOPSIS
        依分发表项把入口的绑定参数映射为目标脚本形参（哈希表 splatting，按名传参）。

    .DESCRIPTION
        Map   : 入口形参名 -> 目标形参名（值透传）。
        Always: 即使调用方未显式绑定（走默认值）也须透传的入口形参名（如 -Matrix 默认 default）。
        仅当入口显式绑定 或 出现在 Always 时才透传 —— 与收敛前的 `$PSBoundParameters.ContainsKey` 语义一致。

    .OUTPUTS
        hashtable。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][hashtable]$Entry,
        [Parameter(Mandatory = $true)][hashtable]$BoundParameters
    )

    $always = @($Entry['Always'])
    $params = @{}
    foreach ($key in @($Entry['Map'].Keys)) {
        if ($BoundParameters.ContainsKey($key) -or ($always -contains $key)) {
            $params[[string]$Entry['Map'][$key]] = $BoundParameters[$key]
        }
    }
    # Fixed：常量目标形参（如转发到「单一入口 + -Action」形态时的 Action 选择子）
    if ($Entry.ContainsKey('Fixed')) {
        foreach ($k in @($Entry['Fixed'].Keys)) { $params[[string]$k] = $Entry['Fixed'][$k] }
    }
    return $params
}

function New-SpecCliArgs {
    <#
    .SYNOPSIS
        依分发表项装配 SpecSourceGen.Cli 命令行参数（token 序列）。

    .DESCRIPTION
        Options : @( @{ Param='CasesRoot'; Token='--cases'; Always=$true; Switch=$false }, ... )
        Always  : 未显式绑定也下发（取入口默认值，如 CasesRoot 缺省 = AgentTest）。
        Switch  : 布尔开关（仅下发 token，不带值）。
        仅当入口显式绑定 或 出现在该项 Always 时下发 —— 与收敛前逐项 `if ($PSBoundParameters.ContainsKey)` 一致。

    .OUTPUTS
        string[]。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][hashtable]$Entry,
        [Parameter(Mandatory = $true)][hashtable]$BoundParameters
    )

    $list = New-Object System.Collections.Generic.List[string]
    $list.Add([string]$Entry['Verb'])

    foreach ($opt in @($Entry['Options'])) {
        $isSwitch = $false
        if ($opt.ContainsKey('Switch')) { $isSwitch = [bool]$opt['Switch'] }

        $bound = $BoundParameters.ContainsKey([string]$opt['Param'])
        if (-not $bound -and $opt.ContainsKey('Always') -and [bool]$opt['Always']) { $bound = $true }
        if (-not $bound) { continue }

        $list.Add([string]$opt['Token'])
        if ($isSwitch) { continue }

        $value = $BoundParameters[[string]$opt['Param']]
        if ($opt.ContainsKey('Repeat') -and [bool]$opt['Repeat']) {
            # 多值选项：token 随每个元素重复（如 --source A --source B）
            foreach ($item in @($value)) { $list.Add([string]$item) }
        }
        else {
            $list.Add([string]$value)
        }
    }

    # `,` 必要：单元素/空数组若不加，会随管道解包为标量/$null（消费方随后 -contains 判定即失准）
    return , $list.ToArray()
}

function Import-DispatchTable {
    <#
    .SYNOPSIS
        装载分发表（纯数据 psd1）。
    .PARAMETER Path
        分发表路径。
    .OUTPUTS
        hashtable。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "[Dispatcher] 分发表不存在：$Path"
    }
    return (Import-PowerShellDataFile -LiteralPath $Path)
}

function Get-DispatchEntry {
    <#
    .SYNOPSIS
        按动作名取分发表项；未登记即抛（fail-loud，列出已登记动作）。
    .PARAMETER Table
        分发表（Import-DispatchTable 产物）。
    .PARAMETER Action
        动作名（小写、连字符形态，如 'locate-test'）。
    .OUTPUTS
        hashtable。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][hashtable]$Table,
        [Parameter(Mandatory = $true)][string]$Action
    )

    $entries = [hashtable]$Table['Entries']
    if (-not $entries.ContainsKey($Action)) {
        throw ("[Dispatcher] 未登记的动作：'{0}'（已登记：{1}）" -f $Action, (($entries.Keys | Sort-Object) -join ', '))
    }
    return [hashtable]$entries[$Action]
}

Export-ModuleMember -Function @(
    'Get-NormalizedExitCode',
    'Resolve-DispatchTarget',
    'New-DispatchParams',
    'New-SpecCliArgs',
    'Import-DispatchTable',
    'Get-DispatchEntry'
)