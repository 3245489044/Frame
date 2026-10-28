# @requires: Core/PipRunner, Core/PythonVerifier, Core/EnvManifest
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-providers
# @exports: New-EnvProvider, Get-EnvProvider, Get-EnvItemPython, Get-EnvItemLogPath, Test-EnvVersionSatisfies, Get-EnvPipRequirement, Test-EnvItem, Invoke-EnvItemApply, Test-EnvItemVerify, Get-EnvItemMarkerPath, Get-EnvFileHash

<#
.SYNOPSIS
    Provider 注册与调度 —— 与 Install Framework 同构（Test/Install/Verify），但面向 venv 环境。
#>

function New-EnvProvider {
    param(
        [Parameter(Mandatory = $true)][string]$Type,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Test,
        [Parameter(Mandatory = $true)][scriptblock]$Install,
        [scriptblock]$Verify = $null
    )

    return @{
        'Type'    = $Type
        'Name'    = $Name
        'Test'    = $Test
        'Install' = $Install
        'Verify'  = $Verify
    }
}

function Get-EnvProvider {
    param([Parameter(Mandatory = $true)][string]$Type)

    switch ($Type) {
        'venv'          { return (Get-VenvProvider) }
        'pip-venv'      { return (Get-PipVenvProvider) }
        'pip-req-venv'  { return (Get-PipReqVenvProvider) }
        'git-clone'     { return (Get-GitCloneProvider) }
        'http-file'     { return (Get-HttpFileProvider) }
        'python-verify' { return (Get-PythonVerifyProvider) }
        default         { return $null }
    }
}

function Get-EnvItemPython {
    param([Parameter(Mandatory = $true)]$Item, [Parameter(Mandatory = $true)]$Manifest)

    if ($Item.venv) { return (Resolve-VenvPython -VenvDir ([string]$Item.venv)) }
    return (Get-EnvManifestPython -Manifest $Manifest)
}

function Get-EnvItemLogPath {
    param(
        [Parameter(Mandatory = $true)]$Item,
        [Parameter(Mandatory = $true)]$Manifest,
        [string]$Tag = ''
    )

    $envName = [string]$Manifest.envId
    $itemTag = $Item.name
    if ($Tag) { $itemTag = "$($Item.name)-$Tag" }
    return (Get-EnvPipLogPath -EnvName $envName -Tag $itemTag -Manifest $Manifest)
}

function Test-EnvVersionSatisfies {
    <#
    .SYNOPSIS
        版本判据：支持精确（含 +cu130 后缀）、前缀（2.10 匹配 2.10.0）、>=。
    #>
    param([string]$Actual = '', [string]$Requirement = '')

    if (-not $Requirement) { return $true }
    if (-not $Actual) { return $false }

    $req = $Requirement.Trim()
    $have = $Actual.Trim()
    if ($have -eq $req) { return $true }

    if ($req.StartsWith('>=')) {
        $need = $req.Substring(2).Split('+')[0]
        $haveCore = $have.Split('+')[0]
        try { return ([version]$haveCore -ge [version]$need) } catch { return $false }
    }

    $reqCore = ($req -replace '^==', '').Split('+')[0]
    $haveCore = $have.Split('+')[0]
    if ($haveCore -eq $reqCore) { return $true }
    return $haveCore.StartsWith($reqCore + '.')
}

function Get-EnvPipRequirement {
    param([Parameter(Mandatory = $true)][string]$Package, [string]$Version = '')

    if (-not $Version) { return $Package }
    if ($Version -match '^(>=|<=|==|~=|<|>)') { return "$Package$Version" }
    return "$Package==$Version"
}

function Test-EnvItem {
    <#
    .SYNOPSIS
        探测单项状态；返回 { Installed; Outdated; Version; Reason }。
    #>
    param([Parameter(Mandatory = $true)]$Item, [Parameter(Mandatory = $true)]$Manifest)

    $provider = Get-EnvProvider -Type ([string]$Item.type)
    if (-not $provider) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "未知类型: $($Item.type)" }
    }

    try {
        return (& $provider['Test'] $Item $Manifest)
    } catch {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "探测失败: $($_.Exception.Message)" }
    }
}

function Invoke-EnvItemApply {
    param([Parameter(Mandatory = $true)]$Item, [Parameter(Mandatory = $true)]$Manifest)

    $provider = Get-EnvProvider -Type ([string]$Item.type)
    if (-not $provider) {
        Write-EnvFail "$($Item.name): 未知类型 $($Item.type)"
        return $false
    }

    try {
        return [bool](& $provider['Install'] $Item $Manifest)
    } catch {
        Write-EnvFail "$($Item.name): 安装异常 — $($_.Exception.Message)"
        return $false
    }
}

function Test-EnvItemVerify {
    param([Parameter(Mandatory = $true)]$Item, [Parameter(Mandatory = $true)]$Manifest)

    if (-not $Item.verify) {
        return [pscustomobject]@{ Ok = $true; Reason = ''; Output = '' }
    }

    $provider = Get-EnvProvider -Type ([string]$Item.type)
    if ($provider -and $provider['Verify']) {
        try { return (& $provider['Verify'] $Item $Manifest) }
        catch { return [pscustomobject]@{ Ok = $false; Reason = "校验异常: $($_.Exception.Message)"; Output = '' } }
    }

    $python = Get-EnvItemPython -Item $Item -Manifest $Manifest
    return (Test-EnvPythonExpression -Python $python -Code ([string]$Item.verify) `
            -LogPath (Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'verify') -Quiet)
}

function Get-EnvItemMarkerPath {
    param([Parameter(Mandatory = $true)]$Item, [Parameter(Mandatory = $true)]$Manifest)

    $envDir = Join-Path (Get-EnvStateRoot -Manifest $Manifest) ([string]$Manifest.envId)
    if (-not (Test-Path -LiteralPath $envDir)) { New-Item -ItemType Directory -Force -Path $envDir | Out-Null }
    $safeName = ([string]$Item.name) -replace '[^A-Za-z0-9_\.\-]', '_'
    return (Join-Path $envDir ($safeName + '.ok'))
}

function Get-EnvFileHash {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return '' }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}
