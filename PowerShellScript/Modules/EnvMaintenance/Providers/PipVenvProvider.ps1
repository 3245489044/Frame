# @requires: Core/EnvProviders, Core/PipRunner
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-provider-pip
# @exports: Get-PipVenvProvider, Get-PipReqVenvProvider, Get-EnvPackageSpecs

<#
.SYNOPSIS
    pip Provider（venv 维度）—— pip-venv 与 pip-req-venv。
.DESCRIPTION
    包声明支持两种写法：
        "packages": ["gitpython", "rich"]                          （仅包名）
        "packages": [{"name":"torch","version":"2.10.0+cu130"}]    （带钉版本）
    版本判据由 Test-EnvVersionSatisfies 统一（支持 +cu130 精确/前缀/>=）。
#>

function Get-EnvPackageSpecs {
    param([Parameter(Mandatory = $true)]$Item)

    $specs = @()
    foreach ($package in @($Item.packages)) {
        if (-not $package) { continue }
        if ($package -is [string]) {
            $specs += [pscustomobject]@{ Name = $package; Version = '' }
        } elseif ($package.name) {
            $specs += [pscustomobject]@{ Name = [string]$package.name; Version = [string]$package.version }
        }
    }

    if ($specs.Count -eq 0 -and $Item.source) {
        foreach ($name in ([string]$Item.source -split '\s+')) {
            if ($name) { $specs += [pscustomobject]@{ Name = $name; Version = '' } }
        }
    }

    return $specs
}

function Get-PipVenvProvider {
    return (New-EnvProvider -Type 'pip-venv' -Name 'pip-venv Provider' `
        -Test ${function:Test-PipVenvItem} -Install ${function:Install-PipVenvItem})
}

function Test-PipVenvItem {
    param($Item, $Manifest)

    $python = Get-EnvItemPython -Item $Item -Manifest $Manifest
    if (-not (Test-Path -LiteralPath $python)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "venv python 不存在: $python" }
    }

    $specs = Get-EnvPackageSpecs -Item $Item
    if ($specs.Count -eq 0) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = '未声明 packages' }
    }

    $allOk = $true
    $outdated = $false
    $parts = @()
    $missing = @()

    foreach ($spec in $specs) {
        $installed = Get-EnvPipVersion -Python $python -Package $spec.Name -Manifest $Manifest
        $parts += ("{0}={1}" -f $spec.Name, $(if ($installed) { $installed } else { '缺失' }))
        if (-not $installed) {
            $allOk = $false
            $missing += $spec.Name
            continue
        }
        if ($spec.Version -and -not (Test-EnvVersionSatisfies -Actual $installed -Requirement $spec.Version)) {
            $allOk = $false
            $outdated = $true
        }
    }

    $reason = ''
    if ($missing.Count -gt 0) { $reason = '缺失: ' + ($missing -join ', ') }
    elseif ($outdated) { $reason = '版本不满足要求' }

    return [pscustomobject]@{
        Installed = $allOk
        Outdated  = $outdated
        Version   = ($parts -join ', ')
        Reason    = $reason
    }
}

function Install-PipVenvItem {
    param($Item, $Manifest)

    $python = Get-EnvItemPython -Item $Item -Manifest $Manifest
    if (-not (Test-Path -LiteralPath $python)) {
        Write-EnvFail "venv python 不存在: $python（请先执行 venv 项）"
        return $false
    }

    $requirements = @()
    foreach ($spec in (Get-EnvPackageSpecs -Item $Item)) {
        $requirements += (Get-EnvPipRequirement -Package $spec.Name -Version $spec.Version)
    }
    if ($requirements.Count -eq 0) {
        Write-EnvFail '未声明 packages'
        return $false
    }

    Write-EnvStep ("pip install " + ($requirements -join ' '))
    if ($Item.index) { Write-EnvInfo '索引' ([string]$Item.index) }

    $logPath = Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'pip'
    $timeout = 900
    if ($Item.timeoutSeconds) { $timeout = [int]$Item.timeoutSeconds }

    $result = Invoke-EnvPipInstall -Python $python -Packages $requirements -Index ([string]$Item.index) `
        -TimeoutSeconds $timeout -LogPath $logPath -UseCache:([bool]$Item.cache) -Quiet
    if ($result.ExitCode -ne 0) {
        Write-EnvFail "pip 安装失败: $($result.Reason)"
        if ($logPath) { Write-EnvInfo '日志' $logPath }
        return $false
    }

    Write-EnvOk "$($Item.name) 安装完成"
    return $true
}

function Get-PipReqVenvProvider {
    return (New-EnvProvider -Type 'pip-req-venv' -Name 'pip-req-venv Provider' `
        -Test ${function:Test-PipReqVenvItem} -Install ${function:Install-PipReqVenvItem})
}

function Test-PipReqVenvItem {
    param($Item, $Manifest)

    $python = Get-EnvItemPython -Item $Item -Manifest $Manifest
    if (-not (Test-Path -LiteralPath $python)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "venv python 不存在: $python" }
    }

    $requirementsPath = [string]$Item.requirements
    if (-not $requirementsPath) { $requirementsPath = [string]$Item.source }
    if (-not (Test-Path -LiteralPath $requirementsPath)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "requirements 不存在: $requirementsPath" }
    }

    $markerPath = Get-EnvItemMarkerPath -Item $Item -Manifest $Manifest
    $expected = Get-EnvFileHash -Path $requirementsPath
    $actual = ''
    if (Test-Path -LiteralPath $markerPath) { $actual = (Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8).Trim() }

    if ($actual -ne $expected) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = 'requirements 指纹未记录/已变化' }
    }

    return [pscustomobject]@{ Installed = $true; Outdated = $false; Version = $expected.Substring(0, 8); Reason = '' }
}

function Install-PipReqVenvItem {
    param($Item, $Manifest)

    $python = Get-EnvItemPython -Item $Item -Manifest $Manifest
    if (-not (Test-Path -LiteralPath $python)) {
        Write-EnvFail "venv python 不存在: $python（请先执行 venv 项）"
        return $false
    }

    $requirementsPath = [string]$Item.requirements
    if (-not $requirementsPath) { $requirementsPath = [string]$Item.source }
    if (-not (Test-Path -LiteralPath $requirementsPath)) {
        Write-EnvFail "requirements 不存在: $requirementsPath"
        return $false
    }

    Write-EnvStep "pip install -r $requirementsPath"
    $logPath = Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'pip-req'
    $timeout = 1800
    if ($Item.timeoutSeconds) { $timeout = [int]$Item.timeoutSeconds }

    $result = Invoke-EnvPipRequirements -Python $python -Requirements $requirementsPath -Index ([string]$Item.index) `
        -TimeoutSeconds $timeout -LogPath $logPath -UseCache:([bool]$Item.cache) -Quiet
    if ($result.ExitCode -ne 0) {
        Write-EnvFail "pip -r 安装失败: $($result.Reason)"
        if ($logPath) { Write-EnvInfo '日志' $logPath }
        return $false
    }

    $markerPath = Get-EnvItemMarkerPath -Item $Item -Manifest $Manifest
    Set-Content -LiteralPath $markerPath -Value (Get-EnvFileHash -Path $requirementsPath) -Encoding ASCII
    Write-EnvOk "$($Item.name) 安装完成"
    return $true
}
